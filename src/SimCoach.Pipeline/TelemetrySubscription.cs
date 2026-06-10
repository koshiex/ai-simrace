using System.Threading.Channels;
using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline;

/// <summary>
/// One consumer's view of the telemetry stream: a bounded drop-oldest channel fed by
/// <see cref="TelemetryFanOut"/>. A lagging consumer loses the oldest frames (latest data
/// matters most for live coaching) and the loss is counted, never blocking the producer.
/// Dispose to unsubscribe.
/// </summary>
public sealed class TelemetrySubscription : IDisposable
{
    private readonly Channel<TelemetryFrame> _channel;
    private readonly Action<TelemetrySubscription> _onDispose;
    private long _droppedFrames;

    internal TelemetrySubscription(string name, int channelCapacity, Action<TelemetrySubscription> onDispose)
    {
        Name = name;
        _onDispose = onDispose;
        _channel = Channel.CreateBounded<TelemetryFrame>(
            // SingleWriter deliberately NOT set: Dispose/Complete call TryComplete from other
            // threads than the publishing ingest loop, which is writer-side API use.
            new BoundedChannelOptions(channelCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            },
            _ => Interlocked.Increment(ref _droppedFrames));
    }

    /// <summary>Subscriber name used in drop diagnostics.</summary>
    public string Name { get; }

    /// <summary>Frames lost to backpressure since subscribing.</summary>
    public long DroppedFrames => Interlocked.Read(ref _droppedFrames);

    /// <summary>Streams frames until the fan-out completes or <paramref name="ct"/> is cancelled.</summary>
    public IAsyncEnumerable<TelemetryFrame> ReadAllAsync(CancellationToken ct = default) =>
        _channel.Reader.ReadAllAsync(ct);

    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _onDispose(this);
    }

    internal void Write(TelemetryFrame frame) => _channel.Writer.TryWrite(frame);

    internal void Complete() => _channel.Writer.TryComplete();
}
