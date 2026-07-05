using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>
/// Append-only history of reference (PB) parquet snapshots — every PB ever written for a
/// <c>(track, car, weather)</c> triple (ADR-0017). The active pointer lives in <c>references</c>; this is
/// the versioned history behind it, so a past delta can be traced to the exact reference it used.
/// Retention pruning (oldest first) is driven by <c>ReferenceStore</c>.
/// </summary>
public sealed class ReferenceSnapshotRepository
{
    private readonly SqliteConnectionFactory _factory;

    public ReferenceSnapshotRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public void Insert(ReferenceSnapshotRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            INSERT INTO reference_snapshots
              (id, track_id, car_id, weather_bucket, source_session_id, source_lap_number,
               lap_time_ms, parquet_path, created_at_utc)
            VALUES
              (@Id, @TrackId, @CarId, @WeatherBucket, @SourceSessionId, @SourceLapNumber,
               @LapTimeMs, @ParquetPath, @CreatedAtUtc)
            """,
            row);
    }

    /// <summary>
    /// Every snapshot for the triple, oldest first — retention prunes the head, a future progress view
    /// reads the tail.
    /// </summary>
    public IReadOnlyList<ReferenceSnapshotRow> ListByTriple(string trackId, string carId, string weatherBucket)
    {
        using SqliteConnection connection = _factory.Create();
        return
        [
            .. connection.Query<ReferenceSnapshotRow>(
                """
                SELECT * FROM reference_snapshots
                WHERE track_id = @trackId AND car_id = @carId AND weather_bucket = @weatherBucket
                ORDER BY created_at_utc ASC, id ASC
                """,
                new { trackId, carId, weatherBucket }),
        ];
    }
}
