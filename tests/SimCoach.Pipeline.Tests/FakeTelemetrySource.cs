using System.Runtime.CompilerServices;
using System.Threading.Channels;
using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Tests;

/// <summary>Scriptable telemetry source: tests push frames in, the service streams them out.</summary>
internal sealed class FakeTelemetrySource : ITelemetrySource
{
    private readonly Channel<TelemetryFrame> _channel = Channel.CreateUnbounded<TelemetryFrame>();

    public string Sim => "fake";

    public void Emit(TelemetryFrame frame) => _channel.Writer.TryWrite(frame);

    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<TelemetryFrame> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (TelemetryFrame frame in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return frame;
        }
    }
}
