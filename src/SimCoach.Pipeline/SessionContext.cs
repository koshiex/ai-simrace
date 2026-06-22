namespace SimCoach.Pipeline;

/// <summary>Opaque identity of one recording session: an id plus the instant it started.</summary>
public sealed record SessionIdentity(string SessionId, DateTimeOffset StartedAtUtc);

/// <summary>
/// Shared, single-owner session identity (ADR-0011). The producer (<see cref="IngestService"/>)
/// allocates the <see cref="SessionIdentity"/> and resolves <see cref="Ready"/> <em>before</em>
/// publishing the first frame, so every fan-out subscriber sees identity already available — the
/// inter-subscriber race is removed structurally, not merely narrowed. Consumers
/// (<c>McapRecorderService</c>, <c>SessionManager</c>) carry no session paths; they combine the
/// id with their own base directory.
/// </summary>
public sealed class SessionContext
{
    // RunContinuationsAsynchronously: a consumer awaiting Ready must never resume inline on the
    // producer's publish thread.
    private readonly TaskCompletionSource<SessionIdentity> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the producer has allocated identity (before frame #1 is published).</summary>
    public Task<SessionIdentity> Ready => _ready.Task;

    /// <summary>Allocates identity. First call wins; later calls are ignored (idempotent).</summary>
    public void Resolve(string sessionId, DateTimeOffset startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _ready.TrySetResult(new SessionIdentity(sessionId, startedAtUtc));
    }
}
