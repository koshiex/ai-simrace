using System.Threading.Channels;

namespace SimCoach.Reference;

/// <summary>
/// One consumer's view of the compute domain-event stream. Unlike <c>TelemetrySubscription</c> (333 Hz,
/// drop-oldest), this channel is <b>unbounded and lossless</b>: domain events are sparse (a few per
/// corner) and ordering/completeness matter — a dropped <see cref="Contracts.V1.SessionEvent"/> is
/// unacceptable. Dispose to unsubscribe.
/// </summary>
public sealed class DomainEventSubscription : IDisposable
{
    private readonly Channel<DomainEvent> _channel;
    private readonly Action<DomainEventSubscription> _onDispose;

    internal DomainEventSubscription(string name, Action<DomainEventSubscription> onDispose)
    {
        Name = name;
        _onDispose = onDispose;
        _channel = Channel.CreateUnbounded<DomainEvent>(
            // SingleReader: one consumer drains. SingleWriter NOT set: Complete/Dispose call
            // TryComplete from threads other than the publishing compute loop.
            new UnboundedChannelOptions { SingleReader = true });
    }

    /// <summary>Subscriber name, used in diagnostics.</summary>
    public string Name { get; }

    /// <summary>Streams events until the fan-out completes or <paramref name="ct"/> is cancelled.</summary>
    public IAsyncEnumerable<DomainEvent> ReadAllAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _onDispose(this);
    }

    internal void Write(DomainEvent domainEvent) => _channel.Writer.TryWrite(domainEvent);

    internal void Complete() => _channel.Writer.TryComplete();
}
