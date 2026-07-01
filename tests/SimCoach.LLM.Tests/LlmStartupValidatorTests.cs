using FluentAssertions;
using Microsoft.Extensions.Options;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class LlmStartupValidatorTests
{
    private readonly LlmStartupValidator _validator = new();

    [Fact]
    public void Valid_options_pass()
        => _validator.Validate(null, Valid()).Succeeded.Should().BeTrue();

    [Fact]
    public void Missing_rate_fails_rate_coverage()
    {
        LlmOptions options = Valid() with
        {
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["openrouter-google"] = new()
                {
                    BaseUrl = "https://openrouter.test/api/v1/",
                    AuthEnvVar = "K",
                    Rates = new Dictionary<string, ModelRate>(),
                },
                ["openrouter-anthropic"] = Provider("anthropic/claude-sonnet-4.6"),
            },
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("no rate", StringComparison.Ordinal));
    }

    [Fact]
    public void Fallback_cycle_fails_acyclicity()
    {
        LlmOptions options = Valid() with
        {
            Routes = new Dictionary<string, RouteOptions>
            {
                ["corner"] = Route("openrouter-google", "google/gemini-2.5-flash-lite", "debrief"),
                ["debrief"] = Route("openrouter-anthropic", "anthropic/claude-sonnet-4.6", "corner"),
            },
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void Nonpositive_tokens_or_short_timeout_fails()
    {
        LlmOptions options = Valid() with
        {
            Routes = new Dictionary<string, RouteOptions>
            {
                ["corner"] = new()
                {
                    ProviderId = "openrouter-google",
                    ModelId = "google/gemini-2.5-flash-lite",
                    MaxOutputTokens = 0,
                    Timeout = TimeSpan.FromMilliseconds(10),
                },
            },
        };

        ValidateOptionsResult result = _validator.Validate(null, options);

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("MaxOutputTokens", StringComparison.Ordinal));
        result.Failures.Should().Contain(f => f.Contains("Timeout", StringComparison.Ordinal));
    }

    private static LlmOptions Valid()
        => new()
        {
            Routes = new Dictionary<string, RouteOptions>
            {
                ["corner"] = Route("openrouter-google", "google/gemini-2.5-flash-lite"),
                ["debrief"] = Route("openrouter-anthropic", "anthropic/claude-sonnet-4.6"),
            },
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["openrouter-google"] = Provider("google/gemini-2.5-flash-lite"),
                ["openrouter-anthropic"] = Provider("anthropic/claude-sonnet-4.6"),
            },
        };

    private static RouteOptions Route(string providerId, string modelId, string? fallback = null)
        => new()
        {
            ProviderId = providerId,
            ModelId = modelId,
            MaxOutputTokens = 96,
            Timeout = TimeSpan.FromSeconds(2),
            FallbackRouteKey = fallback,
        };

    private static ProviderOptions Provider(string modelId)
        => new()
        {
            BaseUrl = "https://openrouter.test/api/v1/",
            AuthEnvVar = "OPENROUTER_API_KEY",
            Rates = new Dictionary<string, ModelRate>
            {
                [modelId] = new() { InputPerMillion = 0.1m, OutputPerMillion = 0.4m },
            },
        };
}
