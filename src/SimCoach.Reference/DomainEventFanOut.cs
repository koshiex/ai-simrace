using System.Collections.Immutable;

namespace SimCoach.Reference;

/// <summary>
/// Broadcasts every compute <see cref="DomainEvent"/> to all current subscribers, each behind its own
/// unbounded lossless channel (see <see cref="DomainEventSubscription"/>). Mirrors
/// <c>TelemetryFanOut</c> structurally (immutable snapshot swapped under a lock, lock-free
/// <see cref="Publish"/>) but is <b>lossless</b> — a single producer (<see cref="ComputeService"/>)
/// emits sparse, causally-ordered events that downstream (Phase 3) must receive in full and in order.
/// </summary>
public sealed class DomainEventFanOut
{
    private readonly object _gate = new();
    private ImmutableArray<DomainEventSubscription> _subscriptions = [];
    private bool _isCompleted;

    /// <summary>Registers a consumer. A late subscriber sees only events published after this call.</summary>
    /// <exception cref="InvalidOperationException">The fan-out has already completed.</exception>
    public DomainEventSubscription Subscribe(string subscriberName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberName);
        lock (_gate)
        {
            if (_isCompleted)
            {
                throw new InvalidOperationException("The domain-event fan-out has completed; no new subscriptions.");
            }

            DomainEventSubscription subscription = new(subscriberName, RemoveSubscription);
            _subscriptions = _subscriptions.Add(subscription);
            return subscription;
        }
    }

    /// <summary>Delivers the event to every subscriber, in order, losslessly.</summary>
    public void Publish(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ImmutableArray<DomainEventSubscription> snapshot = _subscriptions;
        foreach (DomainEventSubscription subscription in snapshot)
        {
            subscription.Write(domainEvent);
        }
    }

    /// <summary>Completes all subscriber streams; call when compute ends.</summary>
    public void Complete()
    {
        ImmutableArray<DomainEventSubscription> snapshot;
        lock (_gate)
        {
            _isCompleted = true;
            snapshot = _subscriptions;
        }

        foreach (DomainEventSubscription subscription in snapshot)
        {
            subscription.Complete();
        }
    }

    private void RemoveSubscription(DomainEventSubscription subscription)
    {
        lock (_gate)
        {
            _subscriptions = _subscriptions.Remove(subscription);
        }
    }
}
