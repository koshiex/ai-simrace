using FluentAssertions;
using Google.Protobuf;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Mcap;
using SimCoach.Storage.Repositories;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// Phase-2 end-to-end: a synthesized multi-lap Spa session replayed through the REAL wired chain
/// (McapReplaySource → IngestService → TelemetryFanOut → {SessionManager, McapRecorderService,
/// ComputeService}) produces lap/session rows, a <c>laps.parquet</c>, an established reference, and a
/// coherent domain-event stream. Assertions are structural (counts + key fields) so platform float
/// differences across the win+mac CI matrix never make the golden flaky.
/// </summary>
public sealed class Phase2ComputeE2EGoldenTests : IDisposable
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(20);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simcoach-e2e-" + Guid.NewGuid().ToString("N"));

    public Phase2ComputeE2EGoldenTests() => Directory.CreateDirectory(Path.Combine(_root, "input"));

    [Theory]
    [InlineData(false)] // frames flip lap_number and position together (idealized)
    [InlineData(true)]  // real ACC ordering — guards against re-introducing a lap_number dependence end-to-end
    public async Task Replayed_spa_session_produces_laps_parquet_reference_and_events(bool injectAccDesync)
    {
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        WriteInputSegment(injectAccDesync ? InjectAccLapCounterDesync(frames) : frames);

        var factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") });
        new DatabaseMigrator(factory).Migrate();
        var sessions = new SessionRepository(factory);
        var laps = new LapRepository(factory);
        var references = new ReferenceRepository(factory);

        // Capacity > frame count so no consumer drops frames at SpeedMultiplier 0 (deterministic structure).
        var fanOut = new TelemetryFanOut(new IngestOptions { SubscriberChannelCapacity = 4096 });
        var domainFanOut = new DomainEventFanOut();
        var context = new SessionContext();
        string recordingsBase = Path.Combine(_root, "recordings");
        var lengths = FakeTrackLengths.Spa();

        var trackModels = new TrackModelStore(
            BakedGeometryFixture.Spa(),
            lengths,
            NullLogger<TrackModelStore>.Instance);
        var referenceStore = new ReferenceStore(
            references,
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
            fanOut, domainFanOut, context, trackModels, new ReferenceLookup(references), referenceStore, laps, lengths,
            new ComputeOptions(), NullLogger<ComputeService>.Instance);

        DomainEventSubscription events = domainFanOut.Subscribe("golden");

        try
        {
            await sessionManager.StartAsync(CancellationToken.None);
            await recorder.StartAsync(CancellationToken.None);
            await compute.StartAsync(CancellationToken.None);
            await RunIngestAsync(source, fanOut, context);
            await compute.ExecuteTask!.WaitAsync(_waitTimeout);
            await recorder.ExecuteTask!.WaitAsync(_waitTimeout);
            await sessionManager.ExecuteTask!.WaitAsync(_waitTimeout);
        }
        finally
        {
            // Stop in production order: producer → compute → recorder → SessionManager (finalizes last).
            await compute.StopAsync(CancellationToken.None);
            await recorder.StopAsync(CancellationToken.None);
            await sessionManager.StopAsync(CancellationToken.None);
            compute.Dispose();
            recorder.Dispose();
            sessionManager.Dispose();
        }

        List<DomainEvent> collected = [];
        await foreach (DomainEvent e in events.ReadAllAsync())
        {
            collected.Add(e);
        }

        SessionIdentity identity = await context.Ready;

        // (a) rows + counts
        IReadOnlyList<LapRow> lapRows = laps.GetBySession(identity.SessionId);
        lapRows.Should().HaveCount(2);
        SessionRow sessionRow = sessions.Get(identity.SessionId)!;
        sessionRow.EndedAtUtc.Should().NotBeNull();
        sessionRow.LapCount.Should().Be(2);

        // (b) laps.parquet produced and readable
        string parquetPath = Path.Combine(recordingsBase, identity.SessionId, "laps.parquet");
        File.Exists(parquetPath).Should().BeTrue();
        sessionRow.ParquetPath.Should().Be(parquetPath);

        // (c) a reference established for the triple, with its parquet on disk
        ReferenceRow? reference = references.GetByTriple("spa", "synthetic_gt3", "dry-warm");
        reference.Should().NotBeNull();
        File.Exists(reference!.ParquetPath).Should().BeTrue();

        // (d) the event stream — structural golden
        collected.Count(e => e.Kind == DomainEventKind.Lap).Should().Be(2);
        collected.Count(e => e.Kind == DomainEventKind.Session).Should().Be(1);
        collected.Should().Contain(e => e.Kind == DomainEventKind.Corner);
        collected.Should().Contain(e => e.Kind == DomainEventKind.Sector);

        // (e) stop-ordering invariant: SessionEvent count == session row count == persisted lap count
        var sessionEvent = (SessionEvent)collected.Single(e => e.Kind == DomainEventKind.Session).Payload;
        sessionEvent.LapCount.Should().Be(sessionRow.LapCount).And.Be(lapRows.Count);
    }

    private static async Task RunIngestAsync(McapReplaySource source, TelemetryFanOut fanOut, SessionContext context)
    {
        var ingest = new IngestService(
            source, fanOut, context, new IngestOptions { SubscriberChannelCapacity = 4096 },
            TimeProvider.System, NullLogger<IngestService>.Instance);
        await ingest.StartAsync(CancellationToken.None);
        await ingest.ExecuteTask!.WaitAsync(_waitTimeout);
        await ingest.StopAsync(CancellationToken.None);
        ingest.Dispose();
    }

    /// <summary>
    /// Rewrites an idealized synthetic stream into the real live-ACC start-line signature: completedLaps
    /// increments one frame BEFORE normalized_car_position wraps, pinned at 1.0 on that frame. This is the
    /// ordering that made the old lap-bump-AND-wrap predicate segment whole sessions to zero laps; the
    /// golden must hold identically under it.
    /// </summary>
    private static IReadOnlyList<TelemetryFrame> InjectAccLapCounterDesync(IReadOnlyList<TelemetryFrame> frames)
    {
        List<TelemetryFrame> result = [.. frames.Select(f => f.Clone())];
        for (int i = 1; i < result.Count; i++)
        {
            if (result[i].LapNumber > result[i - 1].LapNumber)
            {
                result[i - 1].LapNumber = result[i].LapNumber;
                result[i - 1].NormalizedCarPosition = 1f;
            }
        }

        return result;
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
}
