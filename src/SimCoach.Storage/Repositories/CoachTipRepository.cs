using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>
/// Append-only writer for the <c>coach_tips</c> log (PR-G / D8): one row per emitted coaching tip. The read
/// side (<c>GetBySessionAsync</c> / <c>ISessionHistoryRepository</c>) lands in PR-H.
/// </summary>
public sealed class CoachTipRepository
{
    private readonly SqliteConnectionFactory _factory;

    public CoachTipRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public void Insert(CoachTipRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        // Store UTC: generated_at_utc is compared/bucketed lexicographically on the "o" string by later read
        // queries, which is only instant-ordered at +00:00. Normalize defensively (no-op if already UTC).
        CoachTipRow normalized = row with { GeneratedAtUtc = row.GeneratedAtUtc.ToUniversalTime() };

        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            INSERT INTO coach_tips
              (session_id, cadence, corner_id, lap_number, action_id, action_label_short,
               rendered_param, priority_phase, priority_rank, severity, phrase_ru, corner_name,
               source, no_pb_yet, provider_model_id, generated_at_utc)
            VALUES
              (@SessionId, @Cadence, @CornerId, @LapNumber, @ActionId, @ActionLabelShort,
               @RenderedParam, @PriorityPhase, @PriorityRank, @Severity, @PhraseRu, @CornerName,
               @Source, @NoPbYet, @ProviderModelId, @GeneratedAtUtc)
            """,
            normalized);
    }
}
