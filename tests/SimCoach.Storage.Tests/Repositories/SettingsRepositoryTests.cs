using FluentAssertions;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class SettingsRepositoryTests : RepositoryTestBase
{
    private readonly SettingsRepository _settings = null!;

    public SettingsRepositoryTests() => _settings = new SettingsRepository(Factory);

    [Fact]
    public void Set_then_get_round_trips()
    {
        // Act
        _settings.Set("theme", "dark", Now);

        // Assert
        _settings.Get("theme").Should().Be("dark");
    }

    [Fact]
    public void Set_twice_overwrites_value()
    {
        // Arrange
        _settings.Set("theme", "dark", Now);

        // Act
        _settings.Set("theme", "light", Now.AddMinutes(5));

        // Assert
        _settings.Get("theme").Should().Be("light");
    }

    [Fact]
    public void Get_returns_null_when_absent() => _settings.Get("missing").Should().BeNull();
}
