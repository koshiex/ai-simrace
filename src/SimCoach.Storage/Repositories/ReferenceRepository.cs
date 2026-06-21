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
               lap_time_ms, parquet_path, pinned, created_at_utc)
            VALUES
              (@Id, @TrackId, @CarId, @WeatherBucket, @SourceSessionId, @SourceLapNumber,
               @LapTimeMs, @ParquetPath, @Pinned, @CreatedAtUtc)
            ON CONFLICT(track_id, car_id, weather_bucket) DO UPDATE SET
              source_session_id = excluded.source_session_id,
              source_lap_number = excluded.source_lap_number,
              lap_time_ms = excluded.lap_time_ms,
              parquet_path = excluded.parquet_path,
              pinned = excluded.pinned,
              created_at_utc = excluded.created_at_utc
            """,
            row);
    }

    public ReferenceRow? GetByTriple(string trackId, string carId, string weatherBucket)
    {
        using SqliteConnection connection = _factory.Create();
        return connection.QuerySingleOrDefault<ReferenceRow>(
            """
            SELECT * FROM [references]
            WHERE track_id = @trackId AND car_id = @carId AND weather_bucket = @weatherBucket
            """,
            new { trackId, carId, weatherBucket });
    }
}
