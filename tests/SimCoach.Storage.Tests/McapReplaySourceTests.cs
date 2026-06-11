using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SimCoach.Contracts.V1;
using SimCoach.Storage.Mcap;
using Xunit;

namespace SimCoach.Storage.Tests;

public sealed class McapReplaySourceTests : IDisposable
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    private readonly string _sessionDir =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName());

    public McapReplaySourceTests()
    {
        Directory.CreateDirectory(_sessionDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_sessionDir))
        {
            Directory.Delete(_sessionDir, recursive: true);
        }
    }

    [Fact]
    public void Sim_identifier_is_replay()
    {
        // Act
        McapReplaySource source = CreateSource(speedMultiplier: 0);

        // Assert
        source.Sim.Should().Be("replay");
    }

    [Fact]
    public async Task Replayed_session_directory_equals_recorded_stream_across_segments()
    {
        // Arrange — two segments, three + two frames, replayed at maximum speed
        WriteSegment("segment-0000.mcap", [Frame(1), Frame(2), Frame(3)]);
        WriteSegment("segment-0001.mcap", [Frame(4), Frame(5)]);
        McapReplaySource source = CreateSource(speedMultiplier: 0);

        // Act
        List<TelemetryFrame> frames = await CollectAllAsync(source);

        // Assert — frame count, order and payloads identical to what was recorded
        frames.Select(frame => frame.LapNumber).Should().Equal(1, 2, 3, 4, 5);
        for (int lapNumber = 1; lapNumber <= 5; lapNumber++)
        {
            frames[lapNumber - 1].ToByteArray().Should().Equal(Frame(lapNumber).ToByteArray());
        }
    }

    [Fact]
    public async Task Single_segment_file_path_replays_that_file_only()
    {
        // Arrange
        WriteSegment("segment-0000.mcap", [Frame(1)]);
        WriteSegment("segment-0001.mcap", [Frame(2)]);
        McapReplaySource source = new(
            new ReplayOptions
            {
                Path = Path.Combine(_sessionDir, "segment-0001.mcap"),
                SpeedMultiplier = 0,
            },
            TimeProvider.System,
            NullLogger<McapReplaySource>.Instance);

        // Act
        List<TelemetryFrame> frames = await CollectAllAsync(source);

        // Assert
        frames.Select(frame => frame.LapNumber).Should().Equal(2);
    }

    [Fact]
    public async Task Speed_zero_ignores_recorded_gaps_entirely()
    {
        // Arrange — 10 s recorded gap; with any real delay the 5 s collect timeout would fire
        WriteSegment(
            "segment-0000.mcap",
            [Frame(1), Frame(2)],
            logTimesNs: [0UL, 10_000_000_000UL]);
        McapReplaySource source = CreateSource(speedMultiplier: 0);

        // Act
        List<TelemetryFrame> frames = await CollectAllAsync(source);

        // Assert
        frames.Should().HaveCount(2);
    }

    [Fact]
    public async Task Replay_waits_on_the_logical_clock_between_frames()
    {
        // Arrange — 100 ms recorded gap at speed 1, driven by a fake time provider
        WriteSegment(
            "segment-0000.mcap",
            [Frame(1), Frame(2)],
            logTimesNs: [0UL, 100_000_000UL]);
        var timeProvider = new FakeTimeProvider();
        McapReplaySource source = new(
            new ReplayOptions { Path = _sessionDir, SpeedMultiplier = 1 },
            timeProvider,
            NullLogger<McapReplaySource>.Instance);
        List<TelemetryFrame> frames = [];
        using var cts = new CancellationTokenSource(_waitTimeout);
        var consumer = Task.Run(
            async () =>
            {
                await foreach (TelemetryFrame frame in source.ReadAsync(cts.Token))
                {
                    lock (frames)
                    {
                        frames.Add(frame);
                    }
                }
            },
            CancellationToken.None);

        // Act / Assert — first frame arrives without any clock advance
        await WaitForCountAsync(frames, 1);
        await Task.Delay(100, CancellationToken.None); // wall time passes, logical clock frozen
        CountOf(frames).Should().Be(1, "the replay must wait on the logical clock, not wall time");

        TimeSpan advanced = TimeSpan.Zero;
        while (CountOf(frames) < 2 && advanced < TimeSpan.FromSeconds(2))
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(25));
            advanced += TimeSpan.FromMilliseconds(25);
            await Task.Delay(1, CancellationToken.None);
        }

        await WaitForCountAsync(frames, 2); // the released timer may still be propagating
        await consumer.WaitAsync(_waitTimeout);
    }

    [Fact]
    public async Task Recorded_gap_is_capped_by_max_frame_delay()
    {
        // Arrange — 10 minute recorded gap, capped to 200 ms
        WriteSegment(
            "segment-0000.mcap",
            [Frame(1), Frame(2)],
            logTimesNs: [0UL, 600_000_000_000UL]);
        var timeProvider = new FakeTimeProvider();
        McapReplaySource source = new(
            new ReplayOptions
            {
                Path = _sessionDir,
                SpeedMultiplier = 1,
                MaxFrameDelay = TimeSpan.FromMilliseconds(200),
            },
            timeProvider,
            NullLogger<McapReplaySource>.Instance);
        List<TelemetryFrame> frames = [];
        using var cts = new CancellationTokenSource(_waitTimeout);
        var consumer = Task.Run(
            async () =>
            {
                await foreach (TelemetryFrame frame in source.ReadAsync(cts.Token))
                {
                    lock (frames)
                    {
                        frames.Add(frame);
                    }
                }
            },
            CancellationToken.None);

        // Act — advancing past the cap must release the second frame
        await WaitForCountAsync(frames, 1);
        TimeSpan advanced = TimeSpan.Zero;
        while (CountOf(frames) < 2 && advanced < TimeSpan.FromSeconds(1))
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(50));
            advanced += TimeSpan.FromMilliseconds(50);
            await Task.Delay(1, CancellationToken.None);
        }

        // Assert — a 10-minute recorded pause must not stall the replay
        await WaitForCountAsync(frames, 2);
        await consumer.WaitAsync(_waitTimeout);
    }

    [Fact]
    public async Task Missing_path_throws_file_not_found_on_enumeration()
    {
        // Arrange
        McapReplaySource source = new(
            new ReplayOptions { Path = Path.Combine(_sessionDir, "nope"), SpeedMultiplier = 0 },
            TimeProvider.System,
            NullLogger<McapReplaySource>.Instance);

        // Act
        Func<Task> act = async () =>
        {
            await foreach (TelemetryFrame _ in source.ReadAsync(CancellationToken.None))
            {
            }
        };

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Directory_without_segments_throws_file_not_found()
    {
        // Arrange — directory exists but holds no .mcap files
        McapReplaySource source = CreateSource(speedMultiplier: 0);

        // Act
        Func<Task> act = async () =>
        {
            await foreach (TelemetryFrame _ in source.ReadAsync(CancellationToken.None))
            {
            }
        };

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Cancellation_ends_the_stream_gracefully()
    {
        // Arrange
        WriteSegment("segment-0000.mcap", [Frame(1), Frame(2)]);
        McapReplaySource source = CreateSource(speedMultiplier: 0);
        using var cts = new CancellationTokenSource();

        // Act — cancel after the first frame; no OperationCanceledException must escape
        List<TelemetryFrame> frames = [];
        await foreach (TelemetryFrame frame in source.ReadAsync(cts.Token))
        {
            frames.Add(frame);
            await cts.CancelAsync();
        }

        // Assert
        frames.Should().HaveCount(1);
    }

    private McapReplaySource CreateSource(double speedMultiplier) =>
        new(
            new ReplayOptions { Path = _sessionDir, SpeedMultiplier = speedMultiplier },
            TimeProvider.System,
            NullLogger<McapReplaySource>.Instance);

    private void WriteSegment(string fileName, TelemetryFrame[] frames, ulong[]? logTimesNs = null)
    {
        using FileStream stream = File.Create(Path.Combine(_sessionDir, fileName));
        using var writer = new McapWriter(stream);
        ushort schemaId = writer.AddSchema(TelemetryFrame.Descriptor.FullName, "protobuf", []);
        ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
        for (int i = 0; i < frames.Length; i++)
        {
            ulong logTimeNs = logTimesNs is null ? (ulong)i * 3_000_000UL : logTimesNs[i];
            writer.WriteMessage(channelId, (uint)i, logTimeNs, logTimeNs, frames[i].ToByteArray());
        }

        writer.Finish();
    }

    private static TelemetryFrame Frame(int lapNumber) => new() { Sim = "acc", LapNumber = lapNumber };

    private static async Task<List<TelemetryFrame>> CollectAllAsync(McapReplaySource source)
    {
        List<TelemetryFrame> frames = [];
        using var cts = new CancellationTokenSource(_waitTimeout);
        await foreach (TelemetryFrame frame in source.ReadAsync(cts.Token))
        {
            frames.Add(frame);
        }

        return frames;
    }

    private static int CountOf(List<TelemetryFrame> frames)
    {
        lock (frames)
        {
            return frames.Count;
        }
    }

    private static async Task WaitForCountAsync(List<TelemetryFrame> frames, int expected)
    {
        using var cts = new CancellationTokenSource(_waitTimeout);
        while (CountOf(frames) < expected)
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, CancellationToken.None);
        }
    }
}
