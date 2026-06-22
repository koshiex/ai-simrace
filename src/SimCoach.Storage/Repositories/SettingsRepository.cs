using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>Key/value settings store. The caller supplies the timestamp (no hidden clock).</summary>
public sealed class SettingsRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SettingsRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public string? Get(string key)
    {
        using SqliteConnection connection = _factory.Create();
        return connection.QuerySingleOrDefault<string>(
            "SELECT value FROM settings WHERE key = @key", new { key });
    }

    public void Set(string key, string value, DateTimeOffset updatedAtUtc)
    {
        using SqliteConnection connection = _factory.Create();
        connection.Execute(
            """
            INSERT INTO settings (key, value, updated_at_utc)
            VALUES (@key, @value, @updatedAtUtc)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at_utc = excluded.updated_at_utc
            """,
            new { key, value, updatedAtUtc });
    }
}
