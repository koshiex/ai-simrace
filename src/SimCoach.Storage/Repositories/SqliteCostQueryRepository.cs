using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>
/// Dapper aggregates over <c>llm_usage</c>. The day bucket uses <c>substr(ts_utc, 1, 10)</c> rather than
/// SQLite <c>date()</c>, because <c>ts_utc</c> is stored in the round-trippable "o" format (7-digit fractional
/// + offset) whose date-function parsing is unreliable; the first 10 chars are always the ISO date.
/// </summary>
public sealed class SqliteCostQueryRepository : ICostQueryRepository
{
    private readonly SqliteConnectionFactory _factory;
    private readonly TimeProvider _timeProvider;

    public SqliteCostQueryRepository(SqliteConnectionFactory factory, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _factory = factory;
        _timeProvider = timeProvider;
    }

    public async Task<CostSummary> GetSessionCostAsync(string sessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        using SqliteConnection connection = _factory.Create();
        return await connection.QuerySingleAsync<CostSummary>(
            new CommandDefinition(
                """
                SELECT COUNT(*)                          AS CallCount,
                       COALESCE(SUM(cost_usd), 0.0)       AS CostUsd,
                       COALESCE(SUM(input_tokens), 0)     AS InputTokens,
                       COALESCE(SUM(output_tokens), 0)    AS OutputTokens,
                       COALESCE(SUM(cached_input_tokens), 0) AS CachedInputTokens
                FROM llm_usage
                WHERE session_id = @sessionId
                """,
                new { sessionId },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<RollingCost> GetRolling30DayCostAsync(CancellationToken ct)
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-30);
        using SqliteConnection connection = _factory.Create();
        return await connection.QuerySingleAsync<RollingCost>(
            new CommandDefinition(
                """
                SELECT COUNT(*) AS CallCount, COALESCE(SUM(cost_usd), 0.0) AS CostUsd
                FROM llm_usage
                WHERE ts_utc >= @cutoff
                """,
                new { cutoff },
                cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CostByDay>> GetCostByDayAsync(int days, CancellationToken ct)
    {
        DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-days);
        using SqliteConnection connection = _factory.Create();
        IEnumerable<CostByDay> rows = await connection.QueryAsync<CostByDay>(
            new CommandDefinition(
                """
                SELECT substr(ts_utc, 1, 10)        AS Day,
                       COALESCE(SUM(cost_usd), 0.0)  AS CostUsd,
                       COUNT(*)                      AS CallCount
                FROM llm_usage
                WHERE ts_utc >= @cutoff
                GROUP BY substr(ts_utc, 1, 10)
                ORDER BY Day
                """,
                new { cutoff },
                cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<IReadOnlyList<CostByRoute>> GetCostByRouteAsync(DateTimeOffset fromUtc, CancellationToken ct)
    {
        using SqliteConnection connection = _factory.Create();
        IEnumerable<CostByRoute> rows = await connection.QueryAsync<CostByRoute>(
            new CommandDefinition(
                """
                SELECT cadence                          AS RouteKey,
                       provider                         AS ProviderId,
                       model_id                         AS ModelId,
                       COUNT(*)                         AS CallCount,
                       COALESCE(SUM(input_tokens), 0)   AS InputTokens,
                       COALESCE(SUM(output_tokens), 0)  AS OutputTokens,
                       COALESCE(SUM(cached_input_tokens), 0) AS CachedInputTokens,
                       COALESCE(SUM(cost_usd), 0.0)     AS CostUsd
                FROM llm_usage
                WHERE ts_utc >= @fromUtc
                GROUP BY cadence, provider, model_id
                ORDER BY CostUsd DESC
                """,
                new { fromUtc },
                cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }
}
