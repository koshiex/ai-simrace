using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace SimCoach.Storage.Database;

/// <summary>
/// Applies embedded SQL migrations not yet recorded in <c>PRAGMA user_version</c>. Idempotent: a
/// second run with no new migrations is a no-op. Each migration runs in its own transaction.
/// </summary>
public sealed class DatabaseMigrator
{
    private const string ResourceMarker = ".Database.Schema.";

    private readonly SqliteConnectionFactory _factory;
    private readonly IReadOnlyList<Migration> _migrations;

    public DatabaseMigrator(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _migrations = LoadEmbeddedMigrations();
    }

    /// <summary>Brings the database up to the latest embedded migration version.</summary>
    public void Migrate()
    {
        using SqliteConnection connection = _factory.Create();

        long current = ReadUserVersion(connection);
        foreach (Migration migration in _migrations.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using SqliteTransaction transaction = connection.BeginTransaction();

            using (SqliteCommand script = connection.CreateCommand())
            {
                script.Transaction = transaction;
                script.CommandText = migration.Script;
                script.ExecuteNonQuery();
            }

            using (SqliteCommand stamp = connection.CreateCommand())
            {
                stamp.Transaction = transaction;
                // PRAGMA values cannot be bound parameters. The version is an int parsed from an
                // embedded resource name (never user input), so this lone interpolation is safe.
                stamp.CommandText = $"PRAGMA user_version = {migration.Version};";
                stamp.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private static long ReadUserVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static IReadOnlyList<Migration> LoadEmbeddedMigrations()
    {
        Assembly assembly = typeof(DatabaseMigrator).Assembly;
        List<Migration> migrations = [];

        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.Contains(ResourceMarker, StringComparison.Ordinal) ||
                !resource.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName = resource[(resource.LastIndexOf(ResourceMarker, StringComparison.Ordinal) + ResourceMarker.Length)..];
            int version = ParseLeadingVersion(fileName);

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader reader = new(stream);
            migrations.Add(new Migration(version, reader.ReadToEnd()));
        }

        return [.. migrations.OrderBy(m => m.Version)];
    }

    private static int ParseLeadingVersion(string fileName)
    {
        int end = 0;
        while (end < fileName.Length && char.IsAsciiDigit(fileName[end]))
        {
            end++;
        }

        if (end == 0)
        {
            throw new InvalidOperationException(
                $"Migration resource '{fileName}' must start with a numeric version, e.g. '001_initial.sql'.");
        }

        return int.Parse(fileName[..end], CultureInfo.InvariantCulture);
    }

    private readonly record struct Migration(int Version, string Script);
}
