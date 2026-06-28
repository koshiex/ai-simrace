namespace SimCoach.Storage.Repositories;

// COUNT/SUM over SQLite INTEGER columns return Int64, and Dapper's record-constructor materialization needs
// the parameter CLR types to match the reader exactly (it does not narrow Int64→Int32), so counts/tokens are long.

/// <summary>Aggregate cost for one session (Screen 06 / Screen 02 status bar).</summary>
public sealed record CostSummary(
    long CallCount,
    double CostUsd,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens);

/// <summary>Rolling-window spend (FR-072 budget guard / 30-day meter).</summary>
public sealed record RollingCost(long CallCount, double CostUsd);

/// <summary>One day's spend (Screen 06 sparkline). <see cref="Day"/> is an ISO date (<c>YYYY-MM-DD</c>).</summary>
public sealed record CostByDay(string Day, double CostUsd, long CallCount);

/// <summary>Spend grouped by cadence (route) + provider + model. <see cref="ProviderId"/> is nullable for rows
/// written before migration 002.</summary>
public sealed record CostByRoute(
    string RouteKey,
    string? ProviderId,
    string ModelId,
    long CallCount,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    double CostUsd);
