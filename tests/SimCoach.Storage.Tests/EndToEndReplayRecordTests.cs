using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Storage;
using SimCoach.Storage.Mcap;
using Xunit;

namespace SimCoach.Storage.Tests;

/// <summary>
/// The Phase 1 exit criterion: a recorded session replayed through the REAL pipeline
/// (McapReplaySource → IngestService → TelemetryFanOut → McapRecorderService) produces
/// an output recording whose frame payloads are byte-identical to the input.
/// </summary>
public sealed class EndToEndReplayRecordTests : IDisposable
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(10);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName());

    public EndToEndReplayRecordTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "input"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Replayed_session_is_rerecorded_byte_identical()
    {
        // Arrange — an input session of 2 segments / 7 frames with realistic field coverage
        TelemetryFrame[] inputFrames = [.. Enumerable.Range(1, 7).Select(BuildFrame)];
        WriteInputSegment("segment-0000.mcap", inputFrames[..4]);
        WriteInputSegment("segment-0001.mcap", inputFrames[4..]);

        McapReplaySource source = new(
            new ReplayOptions { Path = Path.Combine(_root, "input"), SpeedMultiplier = 0 },
            TimeProvider.System,
            NullLogger<McapReplaySource>.Instance);
        TelemetryFanOut fanOut = new(new IngestOptions());
        SessionContext sessionContext = new(); // producer (ingest) resolves it; recorder consumes it
        string outputBase = Path.Combine(_root, "output");
        McapRecorderService recorder = new(
            fanOut,
            sessionContext,
            new RecordingOptions { BasePath = outputBase, SegmentDuration = TimeSpan.FromMinutes(5) },
            TimeProvider.System,
            NullLogger<McapRecorderService>.Instance);
        IngestService ingest = new(
            source,
            fanOut,
            sessionContext,
            new IngestOptions(),
            TimeProvider.System,
            NullLogger<IngestService>.Instance);

        // Act — recorder first (it subscribes in its constructor), then the pump
        try
        {
            await recorder.StartAsync(CancellationToken.None);
            await ingest.StartAsync(CancellationToken.None);
            await ingest.ExecuteTask!.WaitAsync(_waitTimeout);
            await recorder.ExecuteTask!.WaitAsync(_waitTimeout);
        }
        finally
        {
            // Always release the recorder's file handle, or Dispose's directory cleanup
            // masks the real failure on Windows.
            await ingest.StopAsync(CancellationToken.None);
            await recorder.StopAsync(CancellationToken.None);
            ingest.Dispose();
            recorder.Dispose();
        }

        // Assert — every replayed frame re-recorded, in order, byte-identical
        string sessionDir = Directory.GetDirectories(outputBase).Should().ContainSingle().Subject;
        List<McapMessage> outputMessages = [];
        foreach (string segmentPath in Directory.GetFiles(sessionDir, "*.mcap").Order(StringComparer.Ordinal))
        {
            using FileStream stream = File.OpenRead(segmentPath);
            outputMessages.AddRange(McapSegment.Read(stream).Messages);
        }

        outputMessages.Should().HaveCount(inputFrames.Length);
        for (int i = 0; i < inputFrames.Length; i++)
        {
            outputMessages[i].Data.Should().Equal(
                inputFrames[i].ToByteArray(),
                $"frame {i + 1} must survive the pipeline byte-identical");
            outputMessages[i].LogTimeNs.Should().Be(
                ToUnixNanos(inputFrames[i].T),
                "log time must derive from the frame timestamp");
        }
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
            ulong logTimeNs = ToUnixNanos(frames[i].T);
            writer.WriteMessage(channelId, (uint)i, logTimeNs, logTimeNs, frames[i].ToByteArray());
        }

        writer.Finish();
    }

    private static TelemetryFrame BuildFrame(int lapNumber)
    {
        TelemetryFrame frame = new()
        {
            T = Timestamp.FromDateTimeOffset(
                new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(lapNumber * 3)),
            Sim = "acc",
            TrackId = "spa",
            CarId = "audi_r8_lms_evo_ii",
            WeatherBucket = "dry-warm",
            LapNumber = lapNumber,
            SpeedMps = 60f + lapNumber,
            ThrottlePct = 0.5f,
            Gear = 4,
            Rpm = 7000f,
            GForceG = new Vec3 { X = 1.1f, Y = 0.9f, Z = -0.3f },
        };
        frame.TyreTempC.AddRange([80f, 81f, 82f, 83f]);
        return frame;
    }

    private static ulong ToUnixNanos(Timestamp timestamp) =>
        (ulong)timestamp.Seconds * 1_000_000_000UL + (ulong)timestamp.Nanos;
}
