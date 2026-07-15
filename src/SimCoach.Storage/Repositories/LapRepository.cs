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

    /// <summary>
    /// Every stored CLEAN lap for the <c>(track, car, weather)</c> triple that has a full set of sector
    /// times, with its total lap time and session/lap provenance (M46). "Clean" reuses the persisted
    /// <c>laps.is_clean</c> flag — the single authority set at compute time from
    /// <c>CompletedLap.IsClean</c>, which already excludes out/in-laps, pit laps and invalid (track-limits)
    /// laps — so this method never re-derives that definition. The per-sector best and the outlier /
    /// gain guards are computed by <c>OptimalReferenceBuilder</c> over the returned distribution; storage
    /// only supplies the raw clean-lap sector rows.
    /// </summary>
    public IReadOnlyList<CleanLapSectors> BestSectorsByTriple(string trackId, string carId, string weatherBucket)
    {
        using SqliteConnection connection = _factory.Create();
        IEnumerable<CleanLapSectorQueryRow> rows = connection.Query<CleanLapSectorQueryRow>(
            """
            SELECT l.session_id, l.lap_number, l.lap_time_ms, l.s1_ms, l.s2_ms, l.s3_ms
            FROM laps l
            JOIN sessions s ON s.id = l.session_id
            WHERE s.track_id = @trackId AND s.car_id = @carId AND s.weather_bucket = @weatherBucket
              AND l.is_clean = 1
              AND l.s1_ms IS NOT NULL AND l.s2_ms IS NOT NULL AND l.s3_ms IS NOT NULL
            ORDER BY l.session_id, l.lap_number
            """,
            new { trackId, carId, weatherBucket });

        return [.. rows.Select(r => new CleanLapSectors
        {
            SessionId = r.SessionId,
            LapNumber = r.LapNumber,
            LapTimeMs = r.LapTimeMs,
            SectorTimesMs = [r.S1Ms, r.S2Ms, r.S3Ms],
        })];
    }

    /// <summary>
    /// Flat projection for <see cref="BestSectorsByTriple"/>. The query filters out null sectors, so the
    /// sector columns map to non-nullable ints; Dapper's underscore matching binds the snake_case columns.
    /// </summary>
    private sealed record CleanLapSectorQueryRow
    {
        public required string SessionId { get; init; }
        public required int LapNumber { get; init; }
        public required int LapTimeMs { get; init; }
        public required int S1Ms { get; init; }
        public required int S2Ms { get; init; }
        public required int S3Ms { get; init; }
    }
}
