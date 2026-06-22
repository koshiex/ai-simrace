using FluentAssertions;
using Microsoft.Extensions.Logging;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Pipeline.Tests;

public sealed class IngestServiceTests
{
    private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(5);

    private readonly FakeTelemetrySource _source = new();
    private readonly FakeClock _clock = new();
    private readonly SessionContext _context = new();
    private readonly CollectingLogger<IngestService> _logger = new();

    [Fact]
    public async Task Frames_flow_from_source_to_all_subscribers()
    {
        // Arrange
        TelemetryFanOut fanOut = new(new IngestOptions());
        using TelemetrySubscription recorder = fanOut.Subscribe("recorder");
        using TelemetrySubscription compute = fanOut.Subscribe("compute");
        IngestService service = CreateService(fanOut);
        await service.StartAsync(CancellationToken.None);

        // Act
        _source.Emit(Frame(1));
        _source.Emit(Frame(2));
        _source.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert
        (await CollectAsync(recorder)).Select(f => f.LapNumber).Should().Equal(1, 2);
        (await CollectAsync(compute)).Select(f => f.LapNumber).Should().Equal(1, 2);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Source_completion_completes_all_subscriptions()
    {
        // Arrange
        TelemetryFanOut fanOut = new(new IngestOptions());
        using TelemetrySubscription subscription = fanOut.Subscribe("recorder");
        IngestService service = CreateService(fanOut);
        await service.StartAsync(CancellationToken.None);

        // Act — source ends without any frames
        _source.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);

        // Assert — enumeration completes instead of hanging
        (await CollectAsync(subscription)).Should().BeEmpty();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stopping_the_service_completes_subscriptions_gracefully()
    {
        // Arrange
        TelemetryFanOut fanOut = new(new IngestOptions());
        using TelemetrySubscription subscription = fanOut.Subscribe("recorder");
        IngestService service = CreateService(fanOut);
        await service.StartAsync(CancellationToken.None);
        _source.Emit(Frame(1));

        // Act
        await service.StopAsync(new CancellationTokenSource(_waitTimeout).Token);

        // Assert — the pump finished and the subscription channel completed (CollectAsync
        // returns only on channel completion; an open channel would hit its 5 s timeout)
        service.ExecuteTask!.IsCompleted.Should().BeTrue();
        List<TelemetryFrame> frames = await CollectAsync(subscription);
        frames.Should().HaveCountLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Drop_warnings_are_throttled_to_the_configured_interval()
    {
        // Arrange — capacity 1, nobody reads the subscription → every extra frame drops
        IngestOptions options = new()
        {
            SubscriberChannelCapacity = 1,
            DropLogInterval = TimeSpan.FromSeconds(10),
        };
        TelemetryFanOut fanOut = new(options);
        using TelemetrySubscription subscription = fanOut.Subscribe("stuck");
        IngestService service = CreateService(fanOut, options);
        await service.StartAsync(CancellationToken.None);

        // Act — first burst: drops happen, first warning logs immediately
        for (int lapNumber = 1; lapNumber <= 4; lapNumber++)
        {
            _source.Emit(Frame(lapNumber));
        }

        await WaitForAsync(() => subscription.DroppedFrames >= 3);
        int warningsAfterFirstBurst = WarningCount();

        // second burst inside the throttle window — no new warning
        _source.Emit(Frame(5));
        await WaitForAsync(() => subscription.DroppedFrames >= 4);
        int warningsInsideWindow = WarningCount();

        // clock leaves the window — next drop logs again
        _clock.Advance(TimeSpan.FromSeconds(11));
        _source.Emit(Frame(6));
        await WaitForAsync(() => WarningCount() > warningsInsideWindow);

        // Assert
        warningsAfterFirstBurst.Should().Be(1, "the first drop must be reported immediately");
        warningsInsideWindow.Should().Be(1, "drops inside the throttle window must not spam the log");
        WarningCount().Should().Be(2);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Identity_resolved_before_first_publish_subscriber_sees_no_dropped_opening_frames()
    {
        // Arrange — a subscriber present before the pump starts (mirrors the recorder/SessionManager)
        TelemetryFanOut fanOut = new(new IngestOptions());
        using TelemetrySubscription subscriber = fanOut.Subscribe("session-manager");
        IngestService service = CreateService(fanOut);
        await service.StartAsync(CancellationToken.None);

        // Act — read frame #1 as soon as it arrives
        _source.Emit(Frame(1));
        TelemetryFrame first = await FirstAsync(subscriber);

        // Assert — identity was already resolved by the time frame #1 was observable, no drops
        _context.Ready.IsCompletedSuccessfully.Should().BeTrue(
            "the producer resolves identity before publishing frame #1 (ADR-0011)");
        first.LapNumber.Should().Be(1);
        subscriber.DroppedFrames.Should().Be(0);

        _source.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SessionId_uses_millisecond_timestamp_format()
    {
        // Arrange — FakeClock is pinned at 2026-06-10 12:00:00.000 UTC
        TelemetryFanOut fanOut = new(new IngestOptions());
        IngestService service = CreateService(fanOut);

        // Act
        await service.StartAsync(CancellationToken.None);
        SessionIdentity identity = await _context.Ready.WaitAsync(_waitTimeout);

        // Assert
        identity.SessionId.Should().Be("20260610-120000-000");
        identity.StartedAtUtc.Should().Be(_clock.GetUtcNow());

        _source.Complete();
        await service.ExecuteTask!.WaitAsync(_waitTimeout);
        await service.StopAsync(CancellationToken.None);
    }

    private static async Task<TelemetryFrame> FirstAsync(TelemetrySubscription subscription)
    {
        using var cts = new CancellationTokenSource(_waitTimeout);
        await foreach (TelemetryFrame frame in subscription.ReadAllAsync(cts.Token))
        {
            return frame;
        }

        throw new InvalidOperationException("subscription completed without a frame");
    }

    private IngestService CreateService(TelemetryFanOut fanOut, IngestOptions? options = null) =>
        new(_source, fanOut, _context, options ?? new IngestOptions(), _clock, _logger);

    private int WarningCount() => _logger.Snapshot().Count(entry => entry.Level == LogLevel.Warning);

    private static TelemetryFrame Frame(int lapNumber) => new() { Sim = "fake", LapNumber = lapNumber };

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(_waitTimeout);
        while (!condition())
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(5, CancellationToken.None);
        }
    }

    private static async Task<List<TelemetryFrame>> CollectAsync(TelemetrySubscription subscription)
    {
        List<TelemetryFrame> frames = [];
        using var cts = new CancellationTokenSource(_waitTimeout);
        await foreach (TelemetryFrame frame in subscription.ReadAllAsync(cts.Token))
        {
            frames.Add(frame);
        }

        return frames;
    }
}
