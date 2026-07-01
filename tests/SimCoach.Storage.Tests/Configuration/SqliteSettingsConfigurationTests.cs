using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using SimCoach.Storage.Configuration;
using SimCoach.Storage.Repositories;
using SimCoach.Storage.Tests.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Configuration;

public sealed class SqliteSettingsConfigurationTests : RepositoryTestBase
{
    private readonly SqliteSettingsConfigurationSource _source;
    private readonly IConfigurationRoot _config;
    private readonly SqliteSettingsStore _store;

    public SqliteSettingsConfigurationTests()
    {
        _source = new SqliteSettingsConfigurationSource(Factory);
        _config = new ConfigurationBuilder().Add(_source).Build();
        _store = new SqliteSettingsStore(new SettingsRepository(Factory), _source, TimeProvider.System);
    }

    [Fact]
    public void Absent_override_surfaces_no_config_key()
    {
        _config["Llm:Routes:corner:ModelId"].Should().BeNull();
    }

    [Fact]
    public async Task Writing_model_override_surfaces_mapped_config_key_after_reload()
    {
        await _store.SetModelIdAsync("corner", "google/gemini-3.1-flash-lite", CancellationToken.None);

        _config["Llm:Routes:corner:ModelId"].Should().Be("google/gemini-3.1-flash-lite");
    }

    [Fact]
    public async Task Writing_monthly_budget_maps_to_rules_key()
    {
        await _store.SetMonthlyBudgetUsdAsync(12.50m, CancellationToken.None);

        _config["Coach:Rules:MonthlyBudgetUsd"].Should().Be("12.50");
    }

    [Fact]
    public async Task A_write_raises_the_configuration_reload_token()
    {
        bool reloaded = false;
        ChangeToken.OnChange(_config.GetReloadToken, () => reloaded = true);

        await _store.SetLlmLiveAsync(true, CancellationToken.None);

        reloaded.Should().BeTrue();
        _config["Llm:Live"].Should().Be("true");
    }

    [Fact]
    public async Task Unmapped_settings_keys_are_not_surfaced_as_config()
    {
        // A UI-only key (theme) must not leak into the bound configuration.
        await _store.SetAsync("general.theme", "dark", CancellationToken.None);

        _config["general.theme"].Should().BeNull();
        _config["theme"].Should().BeNull();
    }
}
