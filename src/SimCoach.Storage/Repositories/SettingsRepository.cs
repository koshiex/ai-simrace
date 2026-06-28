using Dapper;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Repositories;

/// <summary>Async key/value settings store. The caller supplies the timestamp (no hidden clock).</summary>
public sealed class SettingsRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SettingsRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using SqliteConnection connection = _factory.Create();
        return await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT value FROM settings WHERE key = @key",
            new { key },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, DateTimeOffset updatedAtUtc, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        using SqliteConnection connection = _factory.Create();
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO settings (key, value, updated_at_utc)
            VALUES (@key, @value, @updatedAtUtc)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at_utc = excluded.updated_at_utc
            """,
            new { key, value, updatedAtUtc },
            cancellationToken: ct)).ConfigureAwait(false);
    }
}
