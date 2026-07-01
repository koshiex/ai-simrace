using FluentAssertions;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class LlmOptionsTests
{
    private static RouteOptions ValidRoute() => new()
    {
        ProviderId = "openrouter-google",
        ModelId = "google/gemini-2.5-flash-lite",
        MaxOutputTokens = 96,
        Timeout = TimeSpan.FromSeconds(2),
    };

    private static ProviderOptions ValidProvider() => new()
    {
        BaseUrl = "https://openrouter.ai/api/v1",
        AuthEnvVar = "OPENROUTER_API_KEY",
    };

    private static LlmOptions Valid() => new()
    {
        Routes = new Dictionary<string, RouteOptions> { ["corner"] = ValidRoute() },
        Providers = new Dictionary<string, ProviderOptions> { ["openrouter-google"] = ValidProvider() },
    };

    [Fact]
    public void Valid_options_pass()
        => Valid().Invoking(o => o.EnsureValid()).Should().NotThrow();

    [Fact]
    public void Default_options_have_empty_dictionaries()
    {
        var options = new LlmOptions();

        options.Routes.Should().BeEmpty();
        options.Providers.Should().BeEmpty();
        options.Live.Should().BeFalse();
    }

    [Fact]
    public void Empty_routes_throw()
        => new LlmOptions { Providers = new Dictionary<string, ProviderOptions> { ["openrouter-google"] = ValidProvider() } }
            .Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();

    [Fact]
    public void Empty_providers_throw()
        => new LlmOptions { Routes = new Dictionary<string, RouteOptions> { ["corner"] = ValidRoute() } }
            .Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();

    [Fact]
    public void Empty_route_key_throws()
    {
        LlmOptions options = Valid() with
        {
            Routes = new Dictionary<string, RouteOptions> { [" "] = ValidRoute() },
        };

        options.Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Recurses_into_a_bad_route()
    {
        LlmOptions options = Valid() with
        {
            Routes = new Dictionary<string, RouteOptions> { ["corner"] = ValidRoute() with { MaxOutputTokens = 0 } },
        };

        options.Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Recurses_into_a_bad_provider()
    {
        LlmOptions options = Valid() with
        {
            Providers = new Dictionary<string, ProviderOptions> { ["openrouter-google"] = ValidProvider() with { BaseUrl = " " } },
        };

        options.Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }
}
