namespace SimCoach.Storage.Configuration;

/// <summary>
/// Lets the runtime settings store re-read the SQLite-backed configuration provider after a write, so an
/// override applies to the next route resolution. Implemented by <see cref="SqliteSettingsConfigurationSource"/>;
/// injected into the settings store so the store stays decoupled from the configuration-source type.
/// </summary>
public interface ISettingsReloadSignal
{
    void Reload();
}
