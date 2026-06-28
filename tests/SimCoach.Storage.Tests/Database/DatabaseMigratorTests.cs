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
            "coach_tips", "laps", "llm_usage", "references", "sessions", "settings");
        indexes.Should().BeEquivalentTo(
            "idx_coach_tips_session", "idx_laps_session", "idx_llm_usage_ts", "idx_sessions_track_car");
        connection.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(3);
    }

    [Fact]
    public void Migrate_creates_coach_tips_with_tip_log_columns()
    {
        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        List<string> columns =
            [.. connection.Query<string>("SELECT name FROM pragma_table_info('coach_tips')")];

        columns.Should().Contain(["session_id", "rendered_param", "priority_rank", "severity", "no_pb_yet"]);
    }

    [Fact]
    public void Migrate_adds_cost_columns_to_llm_usage_without_duplicating_model_id()
    {
        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        List<string> columns =
            [.. connection.Query<string>("SELECT name FROM pragma_table_info('llm_usage')")];

        columns.Should().Contain(["provider", "cached_input_tokens", "model_id"]);
        columns.Count(c => c == "model_id").Should().Be(1);
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 1, 2 })]
    [InlineData(new[] { 1, 2, 3 })]
    public void AssertContiguous_accepts_contiguous_runs(int[] versions)
    {
        Action act = () => DatabaseMigrator.AssertContiguous(versions);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 1, 3 })]
    [InlineData(new[] { 1, 1, 2 })]
    [InlineData(new[] { 1, 2, 4 })]
    public void AssertContiguous_rejects_gapped_or_duplicated_sets(int[] versions)
    {
        Action act = () => DatabaseMigrator.AssertContiguous(versions);
        act.Should().Throw<InvalidOperationException>();
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
        connection.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(3);
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
