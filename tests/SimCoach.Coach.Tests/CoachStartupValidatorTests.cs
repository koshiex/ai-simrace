using FluentAssertions;
using Microsoft.Extensions.Options;
using SimCoach.Coach;
using SimCoach.Coach.Actions;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class CoachStartupValidatorTests
{
    [Fact]
    public void Valid_config_passes_all_checks()
    {
        CoachStartupValidator validator = new(Llm(includeStrategy: true), Options.Create(new PromptOptions()), ActionRegistry.Load());

        validator.Validate(null, new CoachOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Missing_strategy_route_fails_route_cadence_completeness()
    {
        CoachStartupValidator validator = new(Llm(includeStrategy: false), Options.Create(new PromptOptions()), ActionRegistry.Load());

        ValidateOptionsResult result = validator.Validate(null, new CoachOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("strategy", StringComparison.Ordinal));
    }

    [Fact]
    public void Unresolvable_prompt_version_fails_prompt_resource_check()
    {
        IOptions<PromptOptions> badPrompts = Options.Create(new PromptOptions
        {
            Cadences = new Dictionary<CoachCadence, PromptSelection>
            {
                [CoachCadence.Corner] = new(SystemVersion: "v999"),
                [CoachCadence.Sector] = new(),
                [CoachCadence.Lap] = new(),
                [CoachCadence.Session] = new(),
            },
        });
        CoachStartupValidator validator = new(Llm(includeStrategy: true), badPrompts, ActionRegistry.Load());

        ValidateOptionsResult result = validator.Validate(null, new CoachOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f => f.Contains("Prompt resources", StringComparison.Ordinal));
    }

    private static IOptions<LlmOptions> Llm(bool includeStrategy)
    {
        var routes = new Dictionary<string, RouteOptions>
        {
            ["corner"] = Route(),
            ["sector"] = Route(),
            ["lap"] = Route(),
            ["debrief"] = Route(),
        };
        if (includeStrategy)
        {
            routes["strategy"] = Route();
        }

        return Options.Create(new LlmOptions
        {
            Routes = routes,
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["openrouter-google"] = new()
                {
                    BaseUrl = "https://openrouter.test/api/v1/",
                    AuthEnvVar = "OPENROUTER_API_KEY",
                },
            },
        });
    }

    private static RouteOptions Route()
        => new()
        {
            ProviderId = "openrouter-google",
            ModelId = "google/gemini-2.5-flash-lite",
            MaxOutputTokens = 96,
            Timeout = TimeSpan.FromSeconds(2),
        };
}
