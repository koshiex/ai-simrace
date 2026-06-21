using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;
using Xunit;

namespace SimCoach.Storage.Tests.Database;

/// <summary>
/// Migration runner over a temp-file SQLite database: schema creation, idempotency, FK enforcement.
/// </summary>
public sealed class DatabaseMigratorTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName(), "simcoach.db");

    private readonly SqliteConnectionFactory _factory;

    public DatabaseMigratorTests() =>
        _factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = _dbPath });

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        string? dir = Path.GetDirectoryName(_dbPath);
        if (dir is not null && Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Migrate_creates_all_tables_and_indexes_and_sets_version()
    {
        // Act
        new DatabaseMigrator(_factory).Migrate();

        // Assert
        using SqliteConnection connection = _factory.Create();
        // Exclude SQLite internal tables (e.g. sqlite_sequence, created by AUTOINCREMENT).
        IEnumerable<string> tables = connection.Query<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name");
        IEnumerable<string> indexes = connection.Query<string>(
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name LIKE 'idx_%' ORDER BY name");

        // sqlite_master stores the unquoted name 'references'.
        tables.Should().BeEquivalentTo(
            "laps", "llm_usage", "references", "sessions", "settings");
        indexes.Should().BeEquivalentTo(
            "idx_laps_session", "idx_llm_usage_ts", "idx_sessions_track_car");
        connection.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(1);
    }

    [Fact]
    public void Migrate_is_idempotent()
    {
        // Arrange
        DatabaseMigrator migrator = new(_factory);
        migrator.Migrate();

        // Act
        Action secondRun = migrator.Migrate;

        // Assert
        secondRun.Should().NotThrow();
        using SqliteConnection connection = _factory.Create();
        connection.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(1);
    }

    [Fact]
    public void Foreign_keys_are_enforced()
    {
        // Arrange
        new DatabaseMigrator(_factory).Migrate();

        // Act — a lap referencing a non-existent session must be rejected.
        using SqliteConnection connection = _factory.Create();
        Action orphanInsert = () => connection.Execute(
            "INSERT INTO laps (id, session_id, lap_number, lap_time_ms) VALUES ('l1', 'missing', 1, 90000)");

        // Assert
        orphanInsert.Should().Throw<SqliteException>();
    }
}
