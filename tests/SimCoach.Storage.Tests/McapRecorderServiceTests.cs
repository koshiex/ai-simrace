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

public sealed class McapRecorderServiceTests : IDisposable
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    private readonly string _basePath =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName());

    private readonly FakeClock _clock = new();
    private readonly TelemetryFanOut _fanOut = new(new IngestOptions());

    public void Dispose()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, recursive: true);
        }
    }

    [Fact]
    public async Task Recorded_frames_roundtrip_byte_identical()
    {
        // Arrange
        McapRecorderService service = CreateService();
        await service.StartAsync(CancellationToken.None);
        TelemetryFrame[] frames = [Frame(1), Frame(2), Frame(3)];

        // Act
        foreach (TelemetryFrame frame in frames)
        {
            _fanOut.Publish(frame);
        }

        _fanOut.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert
        string sessionDir = SingleSessionDirectory();
        string segmentPath = Path.Combine(sessionDir, "segment-0000.mcap");
        File.Exists(segmentPath).Should().BeTrue();
        McapSegment segment = ReadSegment(segmentPath);
        segment.Schemas.Should().ContainSingle().Which.Name.Should().Be("simcoach.contracts.v1.TelemetryFrame");
        segment.Channels.Should().ContainSingle().Which.Topic.Should().Be("telemetry");
        segment.Messages.Should().HaveCount(3);
        for (int i = 0; i < frames.Length; i++)
        {
            segment.Messages[i].Data.Should().Equal(frames[i].ToByteArray());
            segment.Messages[i].Sequence.Should().Be((uint)i);
        }

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Message_log_time_comes_from_the_frame_timestamp()
    {
        // Arrange
        DateTimeOffset capturedAt = new(2026, 6, 10, 12, 0, 1, TimeSpan.Zero);
        TelemetryFrame frame = Frame(1);
        frame.T = Timestamp.FromDateTimeOffset(capturedAt);
        McapRecorderService service = CreateService();
        await service.StartAsync(CancellationToken.None);

        // Act
        _fanOut.Publish(frame);
        _fanOut.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert
        McapSegment segment = ReadSegment(Path.Combine(SingleSessionDirectory(), "segment-0000.mcap"));
        ulong expectedNs = (ulong)capturedAt.ToUnixTimeMilliseconds() * 1_000_000UL;
        segment.Messages.Should().ContainSingle().Which.LogTimeNs.Should().Be(expectedNs);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Segments_rotate_after_the_configured_duration()
    {
        // Arrange
        McapRecorderService service = CreateService(TimeSpan.FromSeconds(60));
        await service.StartAsync(CancellationToken.None);

        // Act — first frame opens segment 0; after 61 s the next frame must open segment 1
        _fanOut.Publish(Frame(1));
        await WaitForAsync(() => Directory.Exists(_basePath) && SegmentCount() == 1);
        _clock.Advance(TimeSpan.FromSeconds(61));
        _fanOut.Publish(Frame(2));
        _fanOut.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert
        string sessionDir = SingleSessionDirectory();
        McapSegment first = ReadSegment(Path.Combine(sessionDir, "segment-0000.mcap"));
        McapSegment second = ReadSegment(Path.Combine(sessionDir, "segment-0001.mcap"));
        first.Messages.Should().ContainSingle().Which.Data.Should().Equal(Frame(1).ToByteArray());
        second.Messages.Should().ContainSingle().Which.Data.Should().Equal(Frame(2).ToByteArray());
        second.Schemas.Should().ContainSingle("every segment must be self-contained");
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Finished_segments_log_frame_count_and_effective_rate()
    {
        // Arrange — the manual live-ACC verification reads the rate from this log line
        CollectingLogger<McapRecorderService> logger = new();
        McapRecorderService service = new(
            _fanOut,
            new RecordingOptions { BasePath = _basePath, SegmentDuration = TimeSpan.FromSeconds(60) },
            _clock,
            logger);
        await service.StartAsync(CancellationToken.None);

        // Act — one frame into segment 0, rotate, two frames into segment 1. Gate each rotation on
        // the new segment file: a frame added to an *open* segment has no observable signal, whereas
        // segment creation does — gating on the file keeps the per-segment counts race-free.
        _fanOut.Publish(Frame(1));
        await WaitForAsync(() => SegmentCount() == 1);
        _clock.Advance(TimeSpan.FromSeconds(61));
        _fanOut.Publish(Frame(2));
        await WaitForAsync(() => SegmentCount() == 2);
        _fanOut.Publish(Frame(3)); // same clock as segment 1's start → stays in segment 1
        _fanOut.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert
        IReadOnlyList<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> entries = logger.Snapshot();
        entries.Should().Contain(entry => entry.Message.Contains("Segment 0 finished: 1 frames"));
        entries.Should().Contain(entry => entry.Message.Contains("Segment 1 finished: 2 frames"));
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task No_frames_means_no_session_directory()
    {
        // Arrange
        McapRecorderService service = CreateService();
        await service.StartAsync(CancellationToken.None);

        // Act
        _fanOut.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert
        Directory.Exists(_basePath).Should().BeFalse();
        await service.StopAsync(CancellationToken.None);
    }

    private McapRecorderService CreateService(TimeSpan? segmentDuration = null) =>
        new(
            _fanOut,
            new RecordingOptions
            {
                BasePath = _basePath,
                SegmentDuration = segmentDuration ?? TimeSpan.FromSeconds(60),
            },
            _clock,
            NullLogger<McapRecorderService>.Instance);

    private string SingleSessionDirectory() =>
        Directory.GetDirectories(_basePath).Should().ContainSingle().Subject;

    private int SegmentCount() =>
        Directory.Exists(_basePath)
            ? Directory.GetDirectories(_basePath).Sum(dir => Directory.GetFiles(dir, "*.mcap").Length)
            : 0;

    private static McapSegment ReadSegment(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return McapSegment.Read(stream);
    }

    private static TelemetryFrame Frame(int lapNumber) =>
        new()
        {
            Sim = "acc",
            LapNumber = lapNumber,
            T = Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero)),
        };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(_waitTimeout);
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, CancellationToken.None);
        }
    }
}
