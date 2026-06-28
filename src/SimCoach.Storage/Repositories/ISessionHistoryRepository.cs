namespace SimCoach.Storage.Repositories;

/// <summary>
/// Read side over <c>sessions</c> + <c>coach_tips</c> for the session-history UI (Screen 07). Declared in P3
/// so the contract is stable; the implementation lands in a later phase (P6/P7) when the screen is built.
/// </summary>
public interface ISessionHistoryRepository
{
    Task<IReadOnlyList<SessionSummary>> ListAsync(SessionFilter? filter, CancellationToken ct);

    Task<IReadOnlyList<CoachTipRow>> GetSessionTipsAsync(string sessionId, CancellationToken ct);
}

/// <summary>UI projection of a recorded session for the history list.</summary>
public sealed record SessionSummary(
    string SessionId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    string TrackId,
    string CarId,
    string WeatherBucket,
    int LapCount,
    int CleanLapCount,
    int? PbTimeMs);

/// <summary>Optional filter for the session-history list.</summary>
public sealed record SessionFilter(
    string? TrackId = null,
    string? CarId = null,
    string? WeatherBucket = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null);
