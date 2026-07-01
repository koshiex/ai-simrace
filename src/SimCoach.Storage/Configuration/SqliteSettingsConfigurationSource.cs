using Microsoft.Extensions.Configuration;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Configuration;

/// <summary>
/// Surfaces user override rows from the <c>settings</c> table into <see cref="IConfiguration"/>. The LLM
/// overrides (model swap, <c>Llm:Live</c>, debrief reasoning) re-bind <em>live</em> via
/// <c>IOptionsMonitor&lt;LlmOptions&gt;</c> on <see cref="Reload"/>. The RuleEngine monthly budget
/// (<c>Coach:Rules:MonthlyBudgetUsd</c>) is read from a stored row at <em>startup</em> (the source is opened
/// before <c>Build()</c>); its live no-restart re-bind lands with the P5 settings UI. Added to the config
/// builder <em>before</em> environment variables so a deliberate <c>SIMCOACH_</c> override still wins over a
/// stored row.
/// </summary>
public sealed class SqliteSettingsConfigurationSource : IConfigurationSource, ISettingsReloadSignal
{
    private readonly SqliteConnectionFactory _factory;
    private SqliteSettingsConfigurationProvider? _provider;

    public SqliteSettingsConfigurationSource(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        _provider = new SqliteSettingsConfigurationProvider(_factory);
        return _provider;
    }

    /// <summary>Re-reads the settings table and raises the reload token (called after a settings write).</summary>
    public void Reload() => _provider?.Reload();
}
