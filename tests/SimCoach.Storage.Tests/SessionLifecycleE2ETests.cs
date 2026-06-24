using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage.Database;
using SimCoach.Storage.Mcap;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests;

/// <summary>
/// End-to-end across the C2c seam: a recorded session replayed through the REAL pipeline
/// (McapReplaySource → IngestService → TelemetryFanOut → {SessionManager, McapRecorderService})
/// writes a <c>sessions</c> row whose <c>mcap_path</c> is the very directory the recorder filled,
/// and finalizes it. Identity is allocated once by the producer and shared via SessionContext.
/// </summary>
public sealed class SessionLifecycleE2ETests : IDisposable
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(10);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName());

    public SessionLifecycleE2ETests() => Directory.CreateDirectory(Path.Combine(_root, "input"));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Replayed_session_writes_and_finalizes_a_sessions_row()
    {
        // Arrange — a 5-frame input session and a fully wired storage stack
        WriteInputSegment("segment-0000.mcap", [.. Enumerable.Range(1, 5).Select(BuildFrame)]);

        SqliteConnectionFactory factory =
            new(new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") });
        new DatabaseMigrator(factory).Migrate();
        SessionRepository sessions = new(factory);

        TelemetryFanOut fanOut = new(new IngestOptions());
        SessionContext context = new();
        string recordingsBase = Path.Combine(_root, "recordings");
        McapReplaySource source = new(
            new ReplayOptions { Path = Path.Combine(_root, "input"), SpeedMultiplier = 0 },
            TimeProvider.System,
            NullLogger<McapReplaySource>.Instance);

        SessionManager sessionManager = new(
            context, fanOut, new RecordingOptions { BasePath = recordingsBase }, sessions, new LapRepository(factory),
            new FakeTrackLengths(), TimeProvider.System, NullLogger<SessionManager>.Instance);
        McapRecorderService recorder = new(
            fanOut, context, new RecordingOptions { BasePath = recordingsBase, SegmentDuration = TimeSpan.FromMinutes(5) },
            TimeProvider.System, NullLogger<McapRecorderService>.Instance);
        IngestService ingest = new(
            source, fanOut, context, new IngestOptions(), TimeProvider.System, NullLogger<IngestService>.Instance);

        // Act — subscribers (manager + recorder) start before the producer, mirroring composition
        try
        {
            await sessionManager.StartAsync(CancellationToken.None);
            await recorder.StartAsync(CancellationToken.None);
            await ingest.StartAsync(CancellationToken.None);
            await ingest.ExecuteTask!.WaitAsync(_waitTimeout);
            await sessionManager.ExecuteTask!.WaitAsync(_waitTimeout);
            await recorder.ExecuteTask!.WaitAsync(_waitTimeout);
        }
        finally
        {
            await ingest.StopAsync(CancellationToken.None);
            await recorder.StopAsync(CancellationToken.None);
            await sessionManager.StopAsync(CancellationToken.None);
            ingest.Dispose();
            recorder.Dispose();
            sessionManager.Dispose();
        }

        // Assert — exactly one session row; its mcap_path is the recorder's populated directory
        SessionIdentity identity = await context.Ready;
        SessionRow? row = sessions.Get(identity.SessionId);
        row.Should().NotBeNull();
        row!.McapPath.Should().Be(Path.Combine(recordingsBase, identity.SessionId));
        Directory.Exists(row.McapPath).Should().BeTrue();
        Directory.GetFiles(row.McapPath, "*.mcap").Should().NotBeEmpty();
        row.TrackId.Should().Be("spa");
        row.EndedAtUtc.Should().NotBeNull("the session was finalized when the stream ended");
        row.LapCount.Should().Be(0, "no compute writes laps until PR-E");
    }

    private void WriteInputSegment(string fileName, TelemetryFrame[] frames)
    {
        using FileStream stream = File.Create(Path.Combine(_root, "input", fileName));
        using var writer = new McapWriter(stream);
        byte[] schemaData = McapProtobufSchema.BuildFileDescriptorSet(TelemetryFrame.Descriptor);
        ushort schemaId = writer.AddSchema(TelemetryFrame.Descriptor.FullName, "protobuf", schemaData);
        ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
        for (int i = 0; i < frames.Length; i++)
        {
            ulong logTimeNs = (ulong)frames[i].T.Seconds * 1_000_000_000UL + (ulong)frames[i].T.Nanos;
            writer.WriteMessage(channelId, (uint)i, logTimeNs, logTimeNs, frames[i].ToByteArray());
        }

        writer.Finish();
    }

    private static TelemetryFrame BuildFrame(int lapNumber) => new()
    {
        T = Timestamp.FromDateTimeOffset(
            new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(lapNumber * 3)),
        Sim = "acc",
        TrackId = "spa",
        CarId = "audi_r8_lms_evo_ii",
        WeatherBucket = "dry-warm",
        LapNumber = lapNumber,
    };
}
