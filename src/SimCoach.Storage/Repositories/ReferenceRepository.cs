using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>
/// CRUD over the <c>references</c> table (reserved word — always bracket-quoted). Plain upsert on the
/// <c>(track, car, weather)</c> triple; PB/pinned replacement policy lives in C7, not here.
/// </summary>
public sealed class ReferenceRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ReferenceRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public void Upsert(ReferenceRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            INSERT INTO [references]
              (id, track_id, car_id, weather_bucket, source_session_id, source_lap_number,
               lap_time_ms, parquet_path, pinned, created_at_utc,
               kind, optimal_sector_ms, sector_sources_json)
            VALUES
              (@Id, @TrackId, @CarId, @WeatherBucket, @SourceSessionId, @SourceLapNumber,
               @LapTimeMs, @ParquetPath, @Pinned, @CreatedAtUtc,
               @Kind, @OptimalSectorMs, @SectorSourcesJson)
            ON CONFLICT(track_id, car_id, weather_bucket, kind) DO UPDATE SET
              source_session_id = excluded.source_session_id,
              source_lap_number = excluded.source_lap_number,
              lap_time_ms = excluded.lap_time_ms,
              parquet_path = excluded.parquet_path,
              pinned = excluded.pinned,
              created_at_utc = excluded.created_at_utc,
              optimal_sector_ms = excluded.optimal_sector_ms,
              sector_sources_json = excluded.sector_sources_json
            """,
            row);
    }

    /// <summary>The active reference row for the triple and <paramref name="kind"/> (default
    /// <c>"pb"</c>), or <c>null</c> when none is stored.</summary>
    public ReferenceRow? GetByTriple(string trackId, string carId, string weatherBucket, string kind = "pb")
    {
        using SqliteConnection connection = _factory.Create();
        return connection.QuerySingleOrDefault<ReferenceRow>(
            """
            SELECT * FROM [references]
            WHERE track_id = @trackId AND car_id = @carId AND weather_bucket = @weatherBucket
              AND kind = @kind
            """,
            new { trackId, carId, weatherBucket, kind });
    }

    /// <summary>Every stored reference row of one <paramref name="kind"/> (one per triple), ordered by
    /// triple. Feeds the own-optimal catch-up bake (M46), which iterates the stored <c>"pb"</c> rows to
    /// derive an optimal per triple from historical clean laps.</summary>
    public IReadOnlyList<ReferenceRow> GetAllByKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        using SqliteConnection connection = _factory.Create();
        return [.. connection.Query<ReferenceRow>(
            """
            SELECT * FROM [references]
            WHERE kind = @kind
            ORDER BY track_id, car_id, weather_bucket
            """,
            new { kind })];
    }
}
