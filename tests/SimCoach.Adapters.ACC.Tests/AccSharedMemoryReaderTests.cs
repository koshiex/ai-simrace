using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Adapters.ACC.SharedMemory;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Tests for the poll-thread/channel/reconnect plumbing of <see cref="AccSharedMemoryReader"/>
/// using a fake page source. The test mapper copies the physics packetId into
/// <see cref="TelemetryFrame.LapNumber"/> so emitted frames are traceable to source packets.
/// </summary>
public sealed class AccSharedMemoryReaderTests
{
    private static readonly TimeSpan _collectTimeout = TimeSpan.FromSeconds(5);

    private static readonly AccReaderOptions _fastOptions = new()
    {
        PollInterval = TimeSpan.Zero,
        ReconnectDelay = TimeSpan.FromMilliseconds(1),
    };

    private readonly FakeAccPageSource _source = new();

    [Fact]
    public void Sim_identifier_is_acc()
    {
        // Act
        AccSharedMemoryReader reader = CreateReader();

        // Assert
        reader.Sim.Should().Be("acc");
    }

    [Fact]
    public async Task Emits_one_frame_per_new_physics_packet()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        AccSharedMemoryReader reader = CreateReader();
        List<TelemetryFrame> frames = [];
        using var cts = new CancellationTokenSource(_collectTimeout);

        // Act — packetId stays 1 (deduplicated) until we publish packet 2 after the first frame
        await foreach (TelemetryFrame frame in reader.ReadAsync(cts.Token))
        {
            frames.Add(frame);
            if (frames.Count == 1)
            {
                _source.SetPhysicsPage(PhysicsPageBytes(packetId: 2));
            }

            if (frames.Count >= 2)
            {
                cts.Cancel();
            }
        }

        // Assert
        frames.Select(frame => frame.LapNumber).Should().Equal(1, 2);
        frames.Should().AllSatisfy(frame => frame.Sim.Should().Be("acc"));
    }

    [Fact]
    public async Task Retries_connection_until_the_game_appears()
    {
        // Arrange
        _source.ConnectResults.Enqueue(false);
        _source.ConnectResults.Enqueue(false);
        _source.ConnectResults.Enqueue(true);
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        AccSharedMemoryReader reader = CreateReader();

        // Act
        List<TelemetryFrame> frames = await CollectFramesAsync(reader, expectedCount: 1);

        // Assert
        frames.Should().HaveCount(1);
        _source.ConnectCallCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Reconnects_and_resumes_after_mid_stream_disconnect()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        AccSharedMemoryReader reader = CreateReader();
        List<TelemetryFrame> frames = [];
        using var cts = new CancellationTokenSource(_collectTimeout);

        // Act — drop the connection after the first frame; reconnect re-emits packet 1
        await foreach (TelemetryFrame frame in reader.ReadAsync(cts.Token))
        {
            frames.Add(frame);
            if (frames.Count == 1)
            {
                _source.IsDisconnected = true;
            }

            if (frames.Count >= 2)
            {
                cts.Cancel();
            }
        }

        // Assert
        frames.Should().HaveCountGreaterThanOrEqualTo(2);
        _source.ConnectCallCount.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Already_cancelled_token_completes_without_frames()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        AccSharedMemoryReader reader = CreateReader();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        List<TelemetryFrame> frames = [];
        await foreach (TelemetryFrame frame in reader.ReadAsync(cts.Token))
        {
            frames.Add(frame);
        }

        // Assert
        frames.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_ends_the_stream_gracefully()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        AccSharedMemoryReader reader = CreateReader();

        // Act
        List<TelemetryFrame> frames = await CollectFramesAsync(reader, expectedCount: 1);

        // Assert — CollectFramesAsync cancels after the first frame; reaching here without
        // an OperationCanceledException is the graceful-completion contract
        frames.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Second_concurrent_enumeration_throws()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        AccSharedMemoryReader reader = CreateReader();
        using var cts = new CancellationTokenSource(_collectTimeout);
        IAsyncEnumerator<TelemetryFrame> first = reader.ReadAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        try
        {
            (await first.MoveNextAsync()).Should().BeTrue();

            // Act
            Func<Task> act = async () =>
            {
                await foreach (TelemetryFrame _ in reader.ReadAsync(cts.Token))
                {
                    break;
                }
            };

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            await cts.CancelAsync();
            await first.DisposeAsync();
        }
    }

    private AccSharedMemoryReader CreateReader() =>
        new(
            _source,
            MapSnapshot,
            _fastOptions,
            TimeProvider.System,
            NullLogger<AccSharedMemoryReader>.Instance);

    private static TelemetryFrame MapSnapshot(AccTelemetrySnapshot snapshot) =>
        new()
        {
            Sim = "acc",
            LapNumber = snapshot.Physics.PacketId,
        };

    private static async Task<List<TelemetryFrame>> CollectFramesAsync(AccSharedMemoryReader reader, int expectedCount)
    {
        List<TelemetryFrame> frames = [];
        using var cts = new CancellationTokenSource(_collectTimeout);
        await foreach (TelemetryFrame frame in reader.ReadAsync(cts.Token))
        {
            frames.Add(frame);
            if (frames.Count >= expectedCount)
            {
                cts.Cancel();
            }
        }

        return frames;
    }

    private static byte[] PhysicsPageBytes(int packetId) =>
        new PageFixtureBuilder(AccPhysicsPage.SizeBytes)
            .WithInt32(0, packetId)
            .Build();
}
