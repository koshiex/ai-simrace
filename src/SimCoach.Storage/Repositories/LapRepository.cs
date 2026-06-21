using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>Parameterized CRUD over the <c>laps</c> table.</summary>
public sealed class LapRepository
{
    private readonly SqliteConnectionFactory _factory;

    public LapRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public void Insert(LapRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            INSERT INTO laps
              (id, session_id, lap_number, lap_time_ms, delta_vs_reference_ms, is_pb, is_clean,
               s1_ms, s2_ms, s3_ms, raw_offset_in_mcap)
            VALUES
              (@Id, @SessionId, @LapNumber, @LapTimeMs, @DeltaVsReferenceMs, @IsPb, @IsClean,
               @S1Ms, @S2Ms, @S3Ms, @RawOffsetInMcap)
            """,
            row);
    }

    public IReadOnlyList<LapRow> GetBySession(string sessionId)
    {
        using SqliteConnection connection = _factory.Create();
        return [.. connection.Query<LapRow>(
            "SELECT * FROM laps WHERE session_id = @sessionId ORDER BY lap_number", new { sessionId })];
    }
}
