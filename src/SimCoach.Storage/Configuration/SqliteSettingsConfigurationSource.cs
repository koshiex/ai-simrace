using Microsoft.Extensions.Configuration;
using SimCoach.Storage.Database;

namespace SimCoach.Storage.Configuration;

/// <summary>
/// Surfaces user override rows from the <c>settings</c> table into <see cref="IConfiguration"/> so a
/// settings write (model / budget / live flag) re-binds <c>IOptionsMonitor&lt;LlmOptions&gt;</c> and the
/// RuleEngine budget without a restart. Added to the config builder <em>before</em> environment variables
/// so a deliberate <c>SIMCOACH_</c> override still wins over a stored row.
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
