using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;
using Xunit;

namespace SimCoach.Storage.Tests.Database;

/// <summary>
/// Migration 007 (ADR-0021): the <c>[references]</c> table rebuild that adds the <c>kind</c> discriminator,
/// makes <c>parquet_path</c> nullable, and stamps existing rows <c>kind='pb'</c>.
/// </summary>
public sealed class Migration007Tests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName(), "simcoach.db");

    private readonly SqliteConnectionFactory _factory;

    public Migration007Tests() =>
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
    public void Fresh_create_reaches_version_7_with_the_kind_schema()
    {
        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        connection.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(7);

        List<string> columns =
            [.. connection.Query<string>("SELECT name FROM pragma_table_info('references')")];
        columns.Should().Contain(["kind", "optimal_sector_ms", "sector_sources_json"]);

        // parquet_path is now nullable; kind is NOT NULL.
        NotNullFlag(connection, "parquet_path").Should().Be(0);
        NotNullFlag(connection, "kind").Should().Be(1);

        connection.ExecuteScalar<long>(
            "SELECT count(*) FROM pragma_foreign_key_check('references')").Should().Be(0);
    }

    private static long NotNullFlag(SqliteConnection connection, string column) =>
        connection.ExecuteScalar<long>(
            "SELECT [notnull] FROM pragma_table_info('references') WHERE name = @column",
            new { column });

    [Fact]
    public void Upgrade_from_006_preserves_row_identity_and_stamps_kind_pb()
    {
        SeedVersion6WithReferenceRow();

        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        connection.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(7);

        connection.ExecuteScalar<long>("SELECT count(*) FROM [references]").Should().Be(1);
        connection.ExecuteScalar<string>("SELECT id FROM [references]").Should().Be("ref-1");
        connection.ExecuteScalar<long>("SELECT pinned FROM [references]").Should().Be(1);
        connection.ExecuteScalar<string>("SELECT created_at_utc FROM [references]")
            .Should().Be("2026-06-01T12:00:00.0000000+00:00");
        connection.ExecuteScalar<string>("SELECT kind FROM [references]").Should().Be("pb");
        connection.ExecuteScalar<string>("SELECT parquet_path FROM [references]")
            .Should().Be("/references/spa.parquet");

        connection.ExecuteScalar<long>(
            "SELECT count(*) FROM pragma_foreign_key_check('references')").Should().Be(0);
    }

    [Fact]
    public void Upgrade_from_006_preserves_non_null_source_session_fk()
    {
        SeedVersion6WithReferenceRow(sourceSessionId: "sess-1");

        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        connection.ExecuteScalar<long>("PRAGMA user_version;").Should().Be(7);

        connection.ExecuteScalar<long>("SELECT count(*) FROM [references]").Should().Be(1);
        connection.ExecuteScalar<string>("SELECT id FROM [references]").Should().Be("ref-1");
        connection.ExecuteScalar<long>("SELECT pinned FROM [references]").Should().Be(1);
        connection.ExecuteScalar<string>("SELECT created_at_utc FROM [references]")
            .Should().Be("2026-06-01T12:00:00.0000000+00:00");
        connection.ExecuteScalar<string>("SELECT source_session_id FROM [references]").Should().Be("sess-1");
        connection.ExecuteScalar<string>("SELECT kind FROM [references]").Should().Be("pb");

        // FK survives the table rebuild: source_session_id still points at the seeded sessions row.
        connection.ExecuteScalar<long>(
            "SELECT count(*) FROM pragma_foreign_key_check('references')").Should().Be(0);
    }

    [Fact]
    public void Optimal_row_with_null_optimal_sector_ms_violates_check()
    {
        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        // CHECK (kind <> 'optimal' OR optimal_sector_ms IS NOT NULL): an optimal row MUST carry sectors.
        // parquet_path stays NULL so only this constraint is exercised (the first CHECK is satisfied by kind).
        Action insert = () =>
            InsertV7Reference(connection, "ref-opt", "optimal", parquetPath: null, optimalSectorMs: null);

        insert.Should().Throw<SqliteException>().WithMessage("*CHECK constraint failed*");
    }

    [Fact]
    public void Non_optimal_row_with_null_parquet_path_violates_check()
    {
        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        // CHECK (kind = 'optimal' OR parquet_path IS NOT NULL): a non-optimal (pb) row MUST carry a path.
        // optimal_sector_ms stays NULL so only this constraint is exercised (the second CHECK holds for pb).
        Action insert = () =>
            InsertV7Reference(connection, "ref-pb", "pb", parquetPath: null, optimalSectorMs: null);

        insert.Should().Throw<SqliteException>().WithMessage("*CHECK constraint failed*");
    }

    [Fact]
    public void Optimal_row_with_null_parquet_path_and_non_null_sector_ms_is_accepted()
    {
        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        InsertV7Reference(
            connection, "ref-opt", "optimal", parquetPath: null, optimalSectorMs: "[35000,70000,104500]");

        connection.ExecuteScalar<long>(
            "SELECT count(*) FROM [references] WHERE kind = 'optimal'").Should().Be(1);
    }

    [Fact]
    public void Pb_row_with_non_null_parquet_path_is_accepted()
    {
        new DatabaseMigrator(_factory).Migrate();

        using SqliteConnection connection = _factory.Create();
        InsertV7Reference(
            connection, "ref-pb", "pb", parquetPath: "/references/spa.parquet", optimalSectorMs: null);

        connection.ExecuteScalar<long>(
            "SELECT count(*) FROM [references] WHERE kind = 'pb'").Should().Be(1);
    }

    private static void InsertV7Reference(
        SqliteConnection connection, string id, string kind, string? parquetPath, string? optimalSectorMs) =>
        connection.Execute(
            """
            INSERT INTO [references]
              (id, track_id, car_id, weather_bucket, lap_time_ms, created_at_utc, kind,
               parquet_path, optimal_sector_ms)
            VALUES
              (@id, 'spa', 'synthetic_gt3', 'dry-warm', 104500, '2026-06-01T12:00:00.0000000+00:00',
               @kind, @parquetPath, @optimalSectorMs)
            """,
            new { id, kind, parquetPath, optimalSectorMs });

    /// <summary>
    /// Hand-rolls the pre-007 state: the migration-001 <c>[references]</c> shape (NOT-NULL parquet_path,
    /// <c>UNIQUE(track,car,weather)</c>, no <c>kind</c>) plus a populated row, stamped
    /// <c>user_version=6</c> so the migrator applies only 007. When <paramref name="sourceSessionId"/> is
    /// non-null a matching <c>sessions</c> row is inserted first and the reference's <c>source_session_id</c>
    /// FK points at it; when null the FK is left NULL.
    /// </summary>
    private void SeedVersion6WithReferenceRow(string? sourceSessionId = null)
    {
        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            CREATE TABLE sessions (id TEXT PRIMARY KEY);
            CREATE TABLE [references] (
              id TEXT PRIMARY KEY,
              track_id TEXT NOT NULL,
              car_id TEXT NOT NULL,
              weather_bucket TEXT NOT NULL,
              source_session_id TEXT REFERENCES sessions(id) ON DELETE SET NULL,
              source_lap_number INTEGER,
              lap_time_ms INTEGER NOT NULL,
              parquet_path TEXT NOT NULL,
              pinned INTEGER NOT NULL DEFAULT 0,
              created_at_utc TEXT NOT NULL,
              UNIQUE(track_id, car_id, weather_bucket)
            );
            """);

        if (sourceSessionId is not null)
        {
            connection.Execute("INSERT INTO sessions (id) VALUES (@sourceSessionId);", new { sourceSessionId });
        }

        connection.Execute(
            """
            INSERT INTO [references]
              (id, track_id, car_id, weather_bucket, source_session_id, source_lap_number,
               lap_time_ms, parquet_path, pinned, created_at_utc)
            VALUES
              ('ref-1', 'spa', 'synthetic_gt3', 'dry-warm', @sourceSessionId, 3,
               104500, '/references/spa.parquet', 1, '2026-06-01T12:00:00.0000000+00:00');
            PRAGMA user_version = 6;
            """,
            new { sourceSessionId });
    }
}
