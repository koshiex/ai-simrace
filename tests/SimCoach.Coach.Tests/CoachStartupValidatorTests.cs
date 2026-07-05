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
    public void Gemini_debrief_route_hard_fails()
    {
        // M28: a Gemini-family debrief model strips maxItems from the debrief schema — hard-fail at startup.
        CoachStartupValidator validator = new(
            Llm(includeStrategy: true, debriefModel: "google/gemini-2.5-flash-lite"),
            Options.Create(new PromptOptions()),
            ActionRegistry.Load());

        ValidateOptionsResult result = validator.Validate(null, new CoachOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f =>
            f.Contains("Debrief route", StringComparison.Ordinal) && f.Contains("Gemini", StringComparison.Ordinal));
    }

    [Fact]
    public void Anthropic_debrief_route_passes()
    {
        // The shipped debrief model (anthropic/claude-sonnet-4.6) keeps its schema bounds — must pass.
        CoachStartupValidator validator = new(
            Llm(includeStrategy: true, debriefModel: "anthropic/claude-sonnet-4.6"),
            Options.Create(new PromptOptions()),
            ActionRegistry.Load());

        validator.Validate(null, new CoachOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Gemini_on_debrief_fallback_chain_hard_fails()
    {
        // M28: LlmRouter forwards the identical maxItems-bounded schema down the FallbackRouteKey chain, so a
        // Gemini model reachable only via debrief_fallback strips the bound just as a Gemini lead would.
        CoachStartupValidator validator = new(
            Llm(includeStrategy: true, debriefModel: "anthropic/claude-haiku-4.5",
                debriefFallbackModel: "google/gemini-2.5-flash-lite"),
            Options.Create(new PromptOptions()),
            ActionRegistry.Load());

        ValidateOptionsResult result = validator.Validate(null, new CoachOptions());

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(f =>
            f.Contains("Debrief route", StringComparison.Ordinal) && f.Contains("Gemini", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_gemini_debrief_fallback_chain_passes()
    {
        // A wholly non-Gemini debrief chain (lead + fallback) keeps its schema bounds — must pass.
        CoachStartupValidator validator = new(
            Llm(includeStrategy: true, debriefModel: "anthropic/claude-sonnet-4.6",
                debriefFallbackModel: "anthropic/claude-haiku-4.5"),
            Options.Create(new PromptOptions()),
            ActionRegistry.Load());

        validator.Validate(null, new CoachOptions()).Succeeded.Should().BeTrue();
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

    // Debrief defaults to the shipped non-Gemini model so the baseline config is valid; the family guard is
    // exercised by passing a Gemini debriefModel.
    private static IOptions<LlmOptions> Llm(
        bool includeStrategy, string debriefModel = "anthropic/claude-sonnet-4.6",
        string? debriefFallbackModel = null)
    {
        var routes = new Dictionary<string, RouteOptions>
        {
            ["corner"] = Route(),
            ["sector"] = Route(),
            ["lap"] = Route(),
            ["debrief"] = Route(
                "openrouter-anthropic", debriefModel,
                fallbackRouteKey: debriefFallbackModel is null ? null : "debrief_fallback"),
        };
        if (debriefFallbackModel is not null)
        {
            routes["debrief_fallback"] = Route("openrouter-anthropic", debriefFallbackModel);
        }

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
                ["openrouter-anthropic"] = new()
                {
                    BaseUrl = "https://openrouter.test/api/v1/",
                    AuthEnvVar = "OPENROUTER_API_KEY",
                },
            },
        });
    }

    private static RouteOptions Route(
        string providerId = "openrouter-google", string modelId = "google/gemini-2.5-flash-lite",
        string? fallbackRouteKey = null)
        => new()
        {
            ProviderId = providerId,
            ModelId = modelId,
            MaxOutputTokens = 96,
            Timeout = TimeSpan.FromSeconds(2),
            FallbackRouteKey = fallbackRouteKey,
        };
}
