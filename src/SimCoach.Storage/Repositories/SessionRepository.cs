using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>Parameterized CRUD over the <c>sessions</c> table.</summary>
public sealed class SessionRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SessionRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public void Insert(SessionRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            INSERT INTO sessions
              (id, started_at_utc, ended_at_utc, sim, track_id, car_id, weather_bucket,
               lap_count, clean_lap_count, pb_time_ms, mcap_path, parquet_path, notes)
            VALUES
              (@Id, @StartedAtUtc, @EndedAtUtc, @Sim, @TrackId, @CarId, @WeatherBucket,
               @LapCount, @CleanLapCount, @PbTimeMs, @McapPath, @ParquetPath, @Notes)
            """,
            row);
    }

    public SessionRow? Get(string id)
    {
        using SqliteConnection connection = _factory.Create();
        return connection.QuerySingleOrDefault<SessionRow>(
            "SELECT * FROM sessions WHERE id = @id", new { id });
    }

    /// <summary>Writes the session-end fields (counts, PB, parquet path, ended timestamp).</summary>
    public void Finalize(
        string id,
        DateTimeOffset endedAtUtc,
        int lapCount,
        int cleanLapCount,
        int? pbTimeMs,
        string? parquetPath)
    {
        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            UPDATE sessions
            SET ended_at_utc = @endedAtUtc, lap_count = @lapCount, clean_lap_count = @cleanLapCount,
                pb_time_ms = @pbTimeMs, parquet_path = @parquetPath
            WHERE id = @id
            """,
            new { id, endedAtUtc, lapCount, cleanLapCount, pbTimeMs, parquetPath });
    }

    public void Delete(string id)
    {
        using SqliteConnection connection = _factory.Create();
        connection.Execute("DELETE FROM sessions WHERE id = @id", new { id });
    }
}
