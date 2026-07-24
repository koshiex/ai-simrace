using Dapper;
using FluentAssertions;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Coach;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;
using SimCoach.Contracts.V1;
using SimCoach.LLM;
using SimCoach.Pipeline;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Mcap;
using SimCoach.Storage.Repositories;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// Phase-3 Coach end-to-end: a synthesized Spa session replayed through the real pipeline (replay →
/// ingest → fan-out → {SessionManager, recorder, ComputeService}) plus the live <see cref="CoachService"/> +
/// <see cref="LiveCoachAmbientState"/> and the LLM ring composed via the public <c>AddLlm</c> with
/// <c>Llm:Live=false</c> (the network-free fake provider). Drains deterministically by awaiting each
/// consumer's <c>ExecuteTask</c> (no host lifecycle), then asserts persisted coaching: <c>coach_tips</c>
/// rows (incl. the end-of-session debrief) and session-attributed, zero-cost <c>llm_usage</c> rows.
/// </summary>
public sealed class CoachReplayE2ETests : IDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(20);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simcoach-coach-e2e-" + Guid.NewGuid().ToString("N"));

    public CoachReplayE2ETests() => Directory.CreateDirectory(Path.Combine(_root, "input"));

    [Fact]
    public async Task Replay_produces_coach_tips_and_attributed_zero_cost_llm_usage()
    {
        WriteInputSegment(SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4));

        var factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") });
        new DatabaseMigrator(factory).Migrate();
        var sessions = new SessionRepository(factory);
        var laps = new LapRepository(factory);
        var references = new ReferenceRepository(factory);

        var fanOut = new TelemetryFanOut(new IngestOptions { SubscriberChannelCapacity = 4096 });
        var domainFanOut = new DomainEventFanOut();
        var context = new SessionContext();
        string recordingsBase = Path.Combine(_root, "recordings");
        var lengths = FakeTrackLengths.Spa();

        var trackModels = new TrackModelStore(BakedGeometryFixture.Spa(), lengths, NullLogger<TrackModelStore>.Instance);
        var referenceStore = new ReferenceStore(
            references,
            new ReferenceSnapshotRepository(factory),
            new ReferenceStorageOptions { Directory = Path.Combine(_root, "references") },
            TimeProvider.System,
            NullLogger<ReferenceStore>.Instance);

        var source = new McapReplaySource(
            new ReplayOptions { Path = Path.Combine(_root, "input"), SpeedMultiplier = 0 },
            TimeProvider.System,
            NullLogger<McapReplaySource>.Instance);
        var sessionManager = new SessionManager(
            context, fanOut, new RecordingOptions { BasePath = recordingsBase }, sessions, laps, lengths,
            TimeProvider.System, NullLogger<SessionManager>.Instance);
        var recorder = new McapRecorderService(
            fanOut, context, new RecordingOptions { BasePath = recordingsBase, SegmentDuration = TimeSpan.FromMinutes(5) },
            TimeProvider.System, NullLogger<McapRecorderService>.Instance);
        var compute = new ComputeService(
            fanOut, domainFanOut, context, trackModels, CenterlineGeometryDataset.Load(), AlienLineDataset.Load(),
            new ReferenceLookup(references, NullLogger<ReferenceLookup>.Instance),
            new OptimalReferenceLookup(references, NullLogger<OptimalReferenceLookup>.Instance), referenceStore, laps, lengths,
            new ComputeOptions(), NullLogger<ComputeService>.Instance);

        // The LLM ring via the real public AddLlm (fake provider; Llm:Live=false), sharing the e2e's DB so
        // llm_usage lands in the same SQLite file the assertions read.
        ServiceProvider llmProvider = BuildLlmRing(factory, context);
        ILlmClient llm = llmProvider.GetRequiredService<ILlmClient>();
        ICostQueryRepository costQuery = llmProvider.GetRequiredService<ICostQueryRepository>();

        var coachOptions = new CoachOptions();
        var names = CornerNameMap.Load();
        var ruleOptions = new RuleEngineOptions();
        var sink = new ConsoleTipSink(new CoachTipRepository(factory), NullLogger<ConsoleTipSink>.Instance);
        var ambient = new LiveCoachAmbientState(
            fanOut, new Gt3CarClasses(), references, trackModels, new CornerPhaseResolver(ruleOptions),
            NullLogger<LiveCoachAmbientState>.Instance);
        var coach = new CoachService(
            domainFanOut,
            new GoldArtifactBuilder(names, coachOptions),
            ActionRegistry.Load(),
            new PromptBuilder(coachOptions, new PromptOptions()),
            llm,
            new RuleEngine(ruleOptions, TimeProvider.System),
            [sink],
            ambient,
            names,
            coachOptions,
            costQuery,
            context,
            TimeProvider.System,
            NullLogger<CoachService>.Instance);

        try
        {
            await sessionManager.StartAsync(CancellationToken.None);
            await recorder.StartAsync(CancellationToken.None);
            await ambient.StartAsync(CancellationToken.None);
            await compute.StartAsync(CancellationToken.None);
            await coach.StartAsync(CancellationToken.None);

            await RunIngestAsync(source, fanOut, context);

            // Drain in dependency order: compute completes the domain fan-out, then coach drains it to the
            // final debrief. Awaiting the tasks (not StopAsync) keeps the drain off the cancellation path.
            await compute.ExecuteTask!.WaitAsync(_timeout);
            await ambient.ExecuteTask!.WaitAsync(_timeout);
            await coach.ExecuteTask!.WaitAsync(_timeout);
        }
        finally
        {
            await coach.StopAsync(CancellationToken.None);
            await ambient.StopAsync(CancellationToken.None);
            await compute.StopAsync(CancellationToken.None);
            await recorder.StopAsync(CancellationToken.None);
            await sessionManager.StopAsync(CancellationToken.None);
            coach.Dispose();
            ambient.Dispose();
            compute.Dispose();
            recorder.Dispose();
            sessionManager.Dispose();
            await llmProvider.DisposeAsync();
        }

        using SqliteConnection connection = factory.Create();

        long tips = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM coach_tips");
        tips.Should().BeGreaterThan(0, "the lap + debrief cadences bypass the real-time gates");
        connection.ExecuteScalar<long>("SELECT COUNT(*) FROM coach_tips WHERE cadence = 'Session'")
            .Should().BeGreaterThan(0, "the end-of-session debrief always emits");

        long usage = connection.ExecuteScalar<long>("SELECT COUNT(*) FROM llm_usage");
        usage.Should().BeGreaterThan(0, "every tip path calls the (fake) LLM");
        connection.ExecuteScalar<long>("SELECT COUNT(*) FROM llm_usage WHERE session_id IS NOT NULL")
            .Should().Be(usage, "usage rows are attributed to the session via ISessionIdProvider");
        connection.ExecuteScalar<long>("SELECT COUNT(*) FROM llm_usage WHERE cost_usd = 0")
            .Should().Be(usage, "the offline fake provider is zero-rated");

        SessionIdentity identity = await context.Ready;
        CostSummary cost = await costQuery.GetSessionCostAsync(identity.SessionId, CancellationToken.None);
        cost.CallCount.Should().Be(usage);
    }

    private static ServiceProvider BuildLlmRing(SqliteConnectionFactory factory, SessionContext context)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(factory);
        services.AddSingleton<ISessionIdProvider>(new ContextSessionIds(context));
        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(LlmConfig()).Build();
        services.AddLlm(config);
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> LlmConfig() => new()
    {
        ["Llm:Live"] = "false",
        ["Llm:OfflineProviderId"] = "fake",
        ["Llm:OfflineModelId"] = "fake/local",
        ["Llm:Routes:corner:ProviderId"] = "fake",
        ["Llm:Routes:corner:ModelId"] = "fake/local",
        ["Llm:Routes:corner:MaxOutputTokens"] = "96",
        ["Llm:Routes:corner:Timeout"] = "00:00:02",
        ["Llm:Routes:sector:ProviderId"] = "fake",
        ["Llm:Routes:sector:ModelId"] = "fake/local",
        ["Llm:Routes:sector:MaxOutputTokens"] = "192",
        ["Llm:Routes:sector:Timeout"] = "00:00:02",
        ["Llm:Routes:lap:ProviderId"] = "fake",
        ["Llm:Routes:lap:ModelId"] = "fake/local",
        ["Llm:Routes:lap:MaxOutputTokens"] = "192",
        ["Llm:Routes:lap:Timeout"] = "00:00:03",
        ["Llm:Routes:debrief:ProviderId"] = "fake",
        ["Llm:Routes:debrief:ModelId"] = "fake/local",
        ["Llm:Routes:debrief:MaxOutputTokens"] = "2000",
        ["Llm:Routes:debrief:Timeout"] = "00:00:08",
        ["Llm:Providers:fake:BaseUrl"] = "https://fake.local/",
        ["Llm:Providers:fake:AuthEnvVar"] = "SIMCOACH_FAKE_UNUSED",
        ["Llm:Providers:fake:Rates:fake/local:InputPerMillion"] = "0",
        ["Llm:Providers:fake:Rates:fake/local:OutputPerMillion"] = "0",
        ["Llm:Providers:fake:Rates:fake/local:CachedInputPerMillion"] = "0",
    };

    private static async Task RunIngestAsync(McapReplaySource source, TelemetryFanOut fanOut, SessionContext context)
    {
        var ingest = new IngestService(
            source, fanOut, context, new IngestOptions { SubscriberChannelCapacity = 4096 },
            TimeProvider.System, NullLogger<IngestService>.Instance);
        await ingest.StartAsync(CancellationToken.None);
        await ingest.ExecuteTask!.WaitAsync(_timeout);
        await ingest.StopAsync(CancellationToken.None);
        ingest.Dispose();
    }

    private void WriteInputSegment(IReadOnlyList<TelemetryFrame> frames)
    {
        using FileStream stream = File.Create(Path.Combine(_root, "input", "segment-0000.mcap"));
        using var writer = new McapWriter(stream);
        byte[] schemaData = McapProtobufSchema.BuildFileDescriptorSet(TelemetryFrame.Descriptor);
        ushort schemaId = writer.AddSchema(TelemetryFrame.Descriptor.FullName, "protobuf", schemaData);
        ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
        for (int i = 0; i < frames.Count; i++)
        {
            ulong logTimeNs = ((ulong)frames[i].T.Seconds * 1_000_000_000UL) + (ulong)frames[i].T.Nanos;
            writer.WriteMessage(channelId, (uint)i, logTimeNs, logTimeNs, frames[i].ToByteArray());
        }

        writer.Finish();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class ContextSessionIds(SessionContext context) : ISessionIdProvider
    {
        public string? CurrentSessionId =>
            context.Ready.IsCompletedSuccessfully ? context.Ready.Result.SessionId : null;
    }

    private sealed class Gt3CarClasses : ICarClassProvider
    {
        public bool TryGetCarClass(string carId, out string carClass)
        {
            carClass = "gt3";
            return true;
        }
    }
}
