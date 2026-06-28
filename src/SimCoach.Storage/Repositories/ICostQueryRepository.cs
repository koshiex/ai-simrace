namespace SimCoach.Storage.Repositories;

/// <summary>
/// Read side over <c>llm_usage</c> for the cost UI (Screen 06 / Screen 04 estimates / Screen 02 status bar).
/// Async by the UI contract (FR-036/FR-072), implemented over Dapper's async API.
/// </summary>
public interface ICostQueryRepository
{
    Task<CostSummary> GetSessionCostAsync(string sessionId, CancellationToken ct);

    Task<RollingCost> GetRolling30DayCostAsync(CancellationToken ct);

    Task<IReadOnlyList<CostByDay>> GetCostByDayAsync(int days, CancellationToken ct);

    Task<IReadOnlyList<CostByRoute>> GetCostByRouteAsync(DateTimeOffset fromUtc, CancellationToken ct);
}
