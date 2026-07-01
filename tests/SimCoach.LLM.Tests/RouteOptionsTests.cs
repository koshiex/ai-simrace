using FluentAssertions;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class RouteOptionsTests
{
    private static RouteOptions Valid() => new()
    {
        ProviderId = "openrouter-google",
        ModelId = "google/gemini-2.5-flash-lite",
        MaxOutputTokens = 96,
        Timeout = TimeSpan.FromSeconds(2),
    };

    [Fact]
    public void Valid_options_pass()
        => Valid().Invoking(o => o.EnsureValid()).Should().NotThrow();

    [Fact]
    public void Low_reasoning_round_trips_and_passes()
        => (Valid() with { Reasoning = ReasoningEffort.Low }).Invoking(o => o.EnsureValid())
            .Should().NotThrow();

    [Fact]
    public void Valid_non_null_fallback_passes()
        => (Valid() with { FallbackRouteKey = "corner" }).Invoking(o => o.EnsureValid())
            .Should().NotThrow();

    [Fact]
    public void Empty_provider_id_throws()
        => (Valid() with { ProviderId = " " }).Invoking(o => o.EnsureValid())
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Empty_model_id_throws()
        => (Valid() with { ModelId = "" }).Invoking(o => o.EnsureValid())
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Zero_max_output_tokens_throws()
        => (Valid() with { MaxOutputTokens = 0 }).Invoking(o => o.EnsureValid())
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Sub_100ms_timeout_throws()
        => (Valid() with { Timeout = TimeSpan.FromMilliseconds(50) }).Invoking(o => o.EnsureValid())
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Whitespace_fallback_route_key_throws()
        => (Valid() with { FallbackRouteKey = "  " }).Invoking(o => o.EnsureValid())
            .Should().Throw<InvalidOperationException>();
}
