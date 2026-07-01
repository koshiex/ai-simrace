using FluentAssertions;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class SettingsRepositoryTests : RepositoryTestBase
{
    private readonly SettingsRepository _settings = null!;

    public SettingsRepositoryTests() => _settings = new SettingsRepository(Factory);

    [Fact]
    public async Task Set_then_get_round_trips()
    {
        await _settings.SetAsync("theme", "dark", Now, CancellationToken.None);

        (await _settings.GetAsync("theme", CancellationToken.None)).Should().Be("dark");
    }

    [Fact]
    public async Task Set_twice_overwrites_value()
    {
        await _settings.SetAsync("theme", "dark", Now, CancellationToken.None);

        await _settings.SetAsync("theme", "light", Now.AddMinutes(5), CancellationToken.None);

        (await _settings.GetAsync("theme", CancellationToken.None)).Should().Be("light");
    }

    [Fact]
    public async Task Get_returns_null_when_absent() =>
        (await _settings.GetAsync("missing", CancellationToken.None)).Should().BeNull();
}
