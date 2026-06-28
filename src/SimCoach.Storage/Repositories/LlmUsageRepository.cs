using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>Append-only writer for the <c>llm_usage</c> cost ledger (PR-F / D6).</summary>
public sealed class LlmUsageRepository
{
    private readonly SqliteConnectionFactory _factory;

    public LlmUsageRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public void Insert(LlmUsageRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // ts_utc must be stored UTC: the cost queries compare/bucket it lexicographically on the "o" string,
        // which is only instant-ordered when every offset is +00:00. Normalize defensively (no-op if already UTC).
        LlmUsageRow normalized = row with { TsUtc = row.TsUtc.ToUniversalTime() };

        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            INSERT INTO llm_usage
              (session_id, ts_utc, model_id, provider, cadence,
               input_tokens, output_tokens, cached_input_tokens, cost_usd, latency_ms, status)
            VALUES
              (@SessionId, @TsUtc, @ModelId, @Provider, @Cadence,
               @InputTokens, @OutputTokens, @CachedInputTokens, @CostUsd, @LatencyMs, @Status)
            """,
            normalized);
    }
}
