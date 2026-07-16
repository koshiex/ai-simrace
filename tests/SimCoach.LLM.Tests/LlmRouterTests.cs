using FluentAssertions;
using Microsoft.Extensions.Options;
using SimCoach.LLM;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class LlmRouterTests
{
    private static readonly LlmRequest _cornerRequest =
        new("corner", "system", "user", "{}", "coach_tip");

    private static RouteOptions CornerRoute() => new()
    {
        ProviderId = "openrouter-google",
        ModelId = "google/gemini-2.5-flash-lite",
        MaxOutputTokens = 96,
        Timeout = TimeSpan.FromSeconds(2),
        Reasoning = ReasoningEffort.Off,
        Stream = false,
        Temperature = 0,
        TopP = 1.0,
    };

    // Live=true so the router resolves the route's own provider/model (these tests exercise live routing; the
    // Live=false offline redirect is covered separately).
    private static IOptionsMonitor<LlmOptions> OptionsWith(RouteOptions route) =>
        new StaticOptionsMonitor<LlmOptions>(new LlmOptions
        {
            Live = true,
            Routes = new Dictionary<string, RouteOptions> { ["corner"] = route },
            Providers = new Dictionary<string, ProviderOptions>(),
        });

    [Fact]
    public async Task Resolves_route_and_delegates_to_provider()
    {
        var router = new LlmRouter(
            OptionsWith(CornerRoute()),
            new Dictionary<string, ILlmProvider> { ["openrouter-google"] = new CaptureProvider() });

        LlmResult result = await router.CompleteAsync(_cornerRequest, CancellationToken.None);

        result.Should().BeOfType<LlmResult.Success>();
    }

    [Fact]
    public async Task Passes_resolved_knobs_to_provider()
    {
        var capture = new CaptureProvider();
        var router = new LlmRouter(
            OptionsWith(CornerRoute()),
            new Dictionary<string, ILlmProvider> { ["openrouter-google"] = capture });

        await router.CompleteAsync(_cornerRequest, CancellationToken.None);

        capture.LastRoute.Should().NotBeNull();
        ResolvedRoute route = capture.LastRoute!.Value;
        route.ProviderId.Should().Be("openrouter-google");
        route.ModelId.Should().Be("google/gemini-2.5-flash-lite");
        route.MaxOutputTokens.Should().Be(96);
        route.Timeout.Should().Be(TimeSpan.FromSeconds(2));
        route.Reasoning.Should().Be(ReasoningEffort.Off);
        route.Stream.Should().BeFalse();
        route.Temperature.Should().Be(0);
        route.TopP.Should().Be(1.0);
        route.CacheSystemPrompt.Should().BeFalse();
    }

    [Fact]
    public async Task Passes_cache_system_prompt_flag_to_provider_when_route_enables_it()
    {
        var capture = new CaptureProvider();
        var router = new LlmRouter(
            OptionsWith(CornerRoute() with { CacheSystemPrompt = true }),
            new Dictionary<string, ILlmProvider> { ["openrouter-google"] = capture });

        await router.CompleteAsync(_cornerRequest, CancellationToken.None);

        capture.LastRoute!.Value.CacheSystemPrompt.Should().BeTrue();
    }

    [Fact]
    public async Task Throws_when_route_key_missing()
    {
        var router = new LlmRouter(
            OptionsWith(CornerRoute()),
            new Dictionary<string, ILlmProvider> { ["openrouter-google"] = new CaptureProvider() });

        Func<Task> act = () => router.CompleteAsync(_cornerRequest with { RouteKey = "nope" }, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Throws_when_provider_for_route_not_registered()
    {
        var router = new LlmRouter(OptionsWith(CornerRoute()), new Dictionary<string, ILlmProvider>());

        Func<Task> act = () => router.CompleteAsync(_cornerRequest, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Offline_resolves_to_the_offline_pair_keeping_route_knobs()
    {
        // Live=false → provider+model swap to the offline pair, but the route's timeout/tokens/reasoning stay.
        var options = new StaticOptionsMonitor<LlmOptions>(new LlmOptions
        {
            Live = false,
            OfflineProviderId = "fake",
            OfflineModelId = "fake/local",
            Routes = new Dictionary<string, RouteOptions> { ["corner"] = CornerRoute() },
            Providers = new Dictionary<string, ProviderOptions>(),
        });
        var capture = new CaptureProvider();
        var router = new LlmRouter(options, new Dictionary<string, ILlmProvider> { ["fake"] = capture });

        await router.CompleteAsync(_cornerRequest, CancellationToken.None);

        ResolvedRoute route = capture.LastRoute!.Value;
        route.ProviderId.Should().Be("fake");
        route.ModelId.Should().Be("fake/local");
        route.MaxOutputTokens.Should().Be(96);             // route knob preserved
        route.Timeout.Should().Be(TimeSpan.FromSeconds(2)); // route knob preserved
    }

    [Fact]
    public void StreamAsync_throws_not_supported()
    {
        var router = new LlmRouter(OptionsWith(CornerRoute()), new Dictionary<string, ILlmProvider>());

        Action act = () => router.StreamAsync(_cornerRequest, CancellationToken.None);

        act.Should().Throw<NotSupportedException>();
    }

    private sealed class CaptureProvider : ILlmProvider
    {
        public ResolvedRoute? LastRoute { get; private set; }

        public Task<LlmResult> CompleteAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
        {
            LastRoute = route;
            var info = new LlmCallInfo(route.ProviderId, route.ModelId, TimeSpan.Zero, "stop");
            return Task.FromResult<LlmResult>(new LlmResult.Success("{}", new LlmUsage(0, 0), info));
        }

        public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
