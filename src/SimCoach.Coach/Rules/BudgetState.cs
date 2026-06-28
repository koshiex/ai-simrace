namespace SimCoach.Coach.Rules;

/// <summary>
/// The spend the budget gate checks: the current session's cost and the rolling 30-day cost. Both are
/// supplied by <c>CoachService</c> from <c>ICostQueryRepository</c> (cached, refreshed after each LLM call —
/// never queried per frame).
/// </summary>
public readonly record struct BudgetState(decimal SessionCostUsd, decimal RollingMonthlyCostUsd)
{
    /// <summary>No spend yet (session start, before any LLM call and with no prior monthly spend).</summary>
    public static BudgetState Zero { get; } = new(0m, 0m);
}
