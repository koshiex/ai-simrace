using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>
/// Writer + per-session reader for the <c>coach_tips</c> log (PR-G / D8, read side PR-H): one row per
/// emitted coaching tip. Async so <c>ConsoleTipSink</c> honours its non-blocking sink contract.
/// </summary>
public sealed class CoachTipRepository
{
    private readonly SqliteConnectionFactory _factory;

    public CoachTipRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task InsertAsync(CoachTipRow row, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(row);

        // Store UTC: generated_at_utc is compared/bucketed lexicographically on the "o" string by the read
        // queries, which is only instant-ordered at +00:00. Normalize defensively (no-op if already UTC).
        CoachTipRow normalized = row with { GeneratedAtUtc = row.GeneratedAtUtc.ToUniversalTime() };

        using SqliteConnection connection = _factory.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO coach_tips
              (session_id, cadence, corner_id, lap_number, action_id, action_label_short,
               rendered_param, priority_phase, priority_rank, severity, phrase_ru, corner_name,
               source, no_pb_yet, provider_model_id, generated_at_utc, top_losses_json, setup_hint)
            VALUES
              (@SessionId, @Cadence, @CornerId, @LapNumber, @ActionId, @ActionLabelShort,
               @RenderedParam, @PriorityPhase, @PriorityRank, @Severity, @PhraseRu, @CornerName,
               @Source, @NoPbYet, @ProviderModelId, @GeneratedAtUtc, @TopLossesJson, @SetupHint)
            """,
            normalized,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>All tips for a session, oldest first (the order they were emitted).</summary>
    public async Task<IReadOnlyList<CoachTipRow>> GetBySessionAsync(string sessionId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        using SqliteConnection connection = _factory.Create();
        IEnumerable<CoachTipRow> rows = await connection.QueryAsync<CoachTipRow>(new CommandDefinition(
            """
            SELECT session_id, cadence, corner_id, lap_number, action_id, action_label_short,
                   rendered_param, priority_phase, priority_rank, severity, phrase_ru, corner_name,
                   source, no_pb_yet, provider_model_id, generated_at_utc, top_losses_json, setup_hint
            FROM coach_tips
            WHERE session_id = @sessionId
            ORDER BY id
            """,
            new { sessionId },
            cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }
}
