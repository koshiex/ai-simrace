using FluentAssertions;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Pipeline.Tests;

public sealed class TelemetryFanOutTests
{
    private static readonly TimeSpan _collectTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Every_subscriber_receives_every_published_frame_in_order()
    {
        // Arrange
        TelemetryFanOut fanOut = new(new IngestOptions());
        using TelemetrySubscription first = fanOut.Subscribe("recorder");
        using TelemetrySubscription second = fanOut.Subscribe("compute");

        // Act
        fanOut.Publish(Frame(1));
        fanOut.Publish(Frame(2));
        fanOut.Publish(Frame(3));
        fanOut.Complete();

        // Assert
        (await CollectAsync(first)).Select(f => f.LapNumber).Should().Equal(1, 2, 3);
        (await CollectAsync(second)).Select(f => f.LapNumber).Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task Slow_subscriber_drops_oldest_frames_and_counts_them()
    {
        // Arrange
        TelemetryFanOut fanOut = new(new IngestOptions { SubscriberChannelCapacity = 2 });
        using TelemetrySubscription subscription = fanOut.Subscribe("slow");

        // Act — 5 frames into capacity 2 with nobody reading
        for (int lapNumber = 1; lapNumber <= 5; lapNumber++)
        {
            fanOut.Publish(Frame(lapNumber));
        }

        fanOut.Complete();

        // Assert — newest survive, oldest dropped
        (await CollectAsync(subscription)).Select(f => f.LapNumber).Should().Equal(4, 5);
        subscription.DroppedFrames.Should().Be(3);
        fanOut.TotalDroppedFrames.Should().Be(3);
    }

    [Fact]
    public async Task Late_subscriber_receives_only_subsequent_frames()
    {
        // Arrange
        TelemetryFanOut fanOut = new(new IngestOptions());
        using TelemetrySubscription early = fanOut.Subscribe("early");
        fanOut.Publish(Frame(1));

        // Act
        using TelemetrySubscription late = fanOut.Subscribe("late");
        fanOut.Publish(Frame(2));
        fanOut.Complete();

        // Assert
        (await CollectAsync(early)).Select(f => f.LapNumber).Should().Equal(1, 2);
        (await CollectAsync(late)).Select(f => f.LapNumber).Should().Equal(2);
    }

    [Fact]
    public async Task Disposed_subscription_stops_receiving_without_breaking_others()
    {
        // Arrange
        TelemetryFanOut fanOut = new(new IngestOptions());
        TelemetrySubscription disposed = fanOut.Subscribe("short-lived");
        using TelemetrySubscription survivor = fanOut.Subscribe("survivor");
        fanOut.Publish(Frame(1));

        // Act
        disposed.Dispose();
        fanOut.Publish(Frame(2));
        fanOut.Complete();

        // Assert
        (await CollectAsync(disposed)).Select(f => f.LapNumber).Should().Equal(1);
        (await CollectAsync(survivor)).Select(f => f.LapNumber).Should().Equal(1, 2);
    }

    [Fact]
    public void Total_dropped_frames_survives_subscriber_disposal()
    {
        // Arrange — capacity 1, publish 3 unread frames → 2 drops on this subscriber
        TelemetryFanOut fanOut = new(new IngestOptions { SubscriberChannelCapacity = 1 });
        TelemetrySubscription subscription = fanOut.Subscribe("short-lived");
        for (int lapNumber = 1; lapNumber <= 3; lapNumber++)
        {
            fanOut.Publish(Frame(lapNumber));
        }

        // Act
        subscription.Dispose();

        // Assert — the total is monotonic; disposing must not erase counted drops
        fanOut.TotalDroppedFrames.Should().Be(2);
    }

    [Fact]
    public void Subscribing_after_completion_fails_fast()
    {
        // Arrange
        TelemetryFanOut fanOut = new(new IngestOptions());
        fanOut.Complete();

        // Act
        Action act = () => fanOut.Subscribe("too-late");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Non_positive_channel_capacity_fails_fast()
    {
        // Act
        Action act = () => _ = new TelemetryFanOut(new IngestOptions { SubscriberChannelCapacity = 0 });

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static TelemetryFrame Frame(int lapNumber) => new() { Sim = "fake", LapNumber = lapNumber };

    private static async Task<List<TelemetryFrame>> CollectAsync(TelemetrySubscription subscription)
    {
        List<TelemetryFrame> frames = [];
        using var cts = new CancellationTokenSource(_collectTimeout);
        await foreach (TelemetryFrame frame in subscription.ReadAllAsync(cts.Token))
        {
            frames.Add(frame);
        }

        return frames;
    }
}
