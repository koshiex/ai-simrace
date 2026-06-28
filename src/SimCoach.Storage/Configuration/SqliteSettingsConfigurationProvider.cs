using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Configuration;

/// <summary>
/// Reads the <c>settings</c> table and maps the override keys onto the configuration paths that bind
/// <c>LlmOptions</c> / the RuleEngine budget. Only mapped keys are surfaced; everything else in the table is
/// runtime-only state read directly via the settings store, not configuration.
/// </summary>
public sealed class SqliteSettingsConfigurationProvider : ConfigurationProvider
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteSettingsConfigurationProvider(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        using SqliteConnection connection = _factory.Create();
        foreach (SettingRow row in connection.Query<SettingRow>("SELECT key, value FROM settings"))
        {
            string? configKey = MapKey(row.Key);
            if (configKey is not null)
            {
                data[configKey] = row.Value;
            }
        }

        Data = data;
    }

    /// <summary>Re-reads the table and raises the reload token so bound options re-bind.</summary>
    public void Reload()
    {
        Load();
        OnReload();
    }

    /// <summary>Maps a settings-table key to the configuration path it overrides, or null if not bound.</summary>
    private static string? MapKey(string settingKey) => settingKey switch
    {
        "model.corner" => "Llm:Routes:corner:ModelId",
        "model.sector" => "Llm:Routes:sector:ModelId",
        "model.lap" => "Llm:Routes:lap:ModelId",
        "model.debrief" => "Llm:Routes:debrief:ModelId",
        "reasoning.debrief" => "Llm:Routes:debrief:Reasoning",
        "budget.monthly_usd" => "Coach:Rules:MonthlyBudgetUsd",
        "llm.live" => "Llm:Live",
        _ => null,
    };

    private sealed record SettingRow(string Key, string Value);
}
