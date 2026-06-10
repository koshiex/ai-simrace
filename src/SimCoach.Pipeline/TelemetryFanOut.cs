using System.Collections.Immutable;
using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline;

/// <summary>
/// Broadcasts every published frame to all current subscribers, each behind its own bounded
/// drop-oldest channel (see <see cref="TelemetrySubscription"/>). Publishing never blocks and
/// never throws on slow consumers. Thread-safe: subscriptions are an immutable snapshot swapped
/// under a lock, so <see cref="Publish"/> iterates lock-free.
/// </summary>
public sealed class TelemetryFanOut
{
    private readonly IngestOptions _options;
    private readonly object _gate = new();
    private ImmutableArray<TelemetrySubscription> _subscriptions = [];
    private long _droppedByRemovedSubscribers;
    private bool _isCompleted;

    public TelemetryFanOut(IngestOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();
        _options = options;
    }

    /// <summary>
    /// Frames lost to backpressure across all subscribers, including already-disposed ones —
    /// the counter is monotonic so throttled drop logging never sees it go backwards.
    /// </summary>
    public long TotalDroppedFrames
    {
        get
        {
            long total = Interlocked.Read(ref _droppedByRemovedSubscribers);
            foreach (TelemetrySubscription subscription in _subscriptions)
            {
                total += subscription.DroppedFrames;
            }

            return total;
        }
    }

    /// <summary>
    /// Registers a consumer. A late subscriber sees only frames published after this call.
    /// </summary>
    /// <exception cref="InvalidOperationException">The fan-out has already completed.</exception>
    public TelemetrySubscription Subscribe(string subscriberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberName);
        lock (_gate)
        {
            if (_isCompleted)
            {
                throw new InvalidOperationException("The telemetry fan-out has completed; no new subscriptions.");
            }

            TelemetrySubscription subscription = new(
                subscriberName, _options.SubscriberChannelCapacity, RemoveSubscription);
            _subscriptions = _subscriptions.Add(subscription);
            return subscription;
        }
    }

    /// <summary>Delivers the frame to every subscriber; lagging ones drop their oldest frame.</summary>
    public void Publish(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ImmutableArray<TelemetrySubscription> snapshot = _subscriptions;
        foreach (TelemetrySubscription subscription in snapshot)
        {
            subscription.Write(frame);
        }
    }

    /// <summary>Completes all subscriber streams; call when the telemetry source ends.</summary>
    public void Complete()
    {
        ImmutableArray<TelemetrySubscription> snapshot;
        lock (_gate)
        {
            _isCompleted = true;
            snapshot = _subscriptions;
        }

        foreach (TelemetrySubscription subscription in snapshot)
        {
            subscription.Complete();
        }
    }

    private void RemoveSubscription(TelemetrySubscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.Contains(subscription))
            {
                return; // double Dispose — drops already folded in
            }

            _subscriptions = _subscriptions.Remove(subscription);
            Interlocked.Add(ref _droppedByRemovedSubscribers, subscription.DroppedFrames);
        }
    }
}
