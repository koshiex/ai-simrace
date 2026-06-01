using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline;

/// <summary>
/// Sim adapter contract. Adapters poll the sim's native telemetry and emit normalised
/// <see cref="TelemetryFrame"/> instances. Each frame's timestamp must be monotonic per session.
/// </summary>
public interface ITelemetrySource
{
    /// <summary>Sim identifier (e.g. "acc", "iracing").</summary>
    string Sim { get; }

    /// <summary>
    /// Stream telemetry frames until the sim disconnects or <paramref name="ct"/> is cancelled.
    /// Implementations may reconnect transparently if the sim restarts.
    /// </summary>
    IAsyncEnumerable<TelemetryFrame> ReadAsync(CancellationToken ct);
}
