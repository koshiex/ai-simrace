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

    private readonly TaskCompletionSource _persisted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when the producer has allocated identity (before frame #1 is published).</summary>
    public Task<SessionIdentity> Ready => _ready.Task;

    /// <summary>
    /// Completes once the <c>sessions</c> row exists in storage. That row needs first-frame metadata
    /// (sim/track/car/weather) and a recording path, so only <c>SessionManager</c> can write it, on its
    /// first frame — later than identity resolves. FK-dependent writers (compute's reference and lap
    /// rows) await this so they never race the insert under fast replay (<c>SpeedMultiplier 0</c>),
    /// where one consumer can drain a whole lap before another has even seen frame #1.
    /// </summary>
    public Task Persisted => _persisted.Task;

    /// <summary>Allocates identity. First call wins; later calls are ignored (idempotent).</summary>
    public void Resolve(string sessionId, DateTimeOffset startedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _ready.TrySetResult(new SessionIdentity(sessionId, startedAtUtc));
    }

    /// <summary>Signals that the session row has been persisted. First call wins; idempotent.</summary>
    public void MarkPersisted() => _persisted.TrySetResult();
}
