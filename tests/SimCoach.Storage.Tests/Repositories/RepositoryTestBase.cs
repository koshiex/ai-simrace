using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Tests.Repositories;

/// <summary>Temp-file SQLite database, migrated to the latest schema, for repository round-trips.</summary>
public abstract class RepositoryTestBase : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName(), "simcoach.db");

    protected SqliteConnectionFactory Factory { get; }

    protected static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    protected RepositoryTestBase()
    {
        Factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = _dbPath });
        new DatabaseMigrator(Factory).Migrate();
    }

    protected static SimCoach.Storage.Repositories.SessionRow Session(string id) => new()
    {
        Id = id,
        StartedAtUtc = Now,
        Sim = "acc",
        TrackId = "spa",
        CarId = "synthetic_gt3",
        WeatherBucket = "dry-warm",
        McapPath = $"/recordings/{id}",
    };

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        string? dir = Path.GetDirectoryName(_dbPath);
        if (dir is not null && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
