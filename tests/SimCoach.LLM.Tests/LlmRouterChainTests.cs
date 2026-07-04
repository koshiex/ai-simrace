using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SimCoach.LLM;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class LlmRouterChainTests
{
    private const string RealTimeSchema =
        """{ "type":"object", "properties": { "action_id": { "enum": ["wider_entry"] } } }""";

    [Fact]
    public async Task Default_route_reaches_fake_provider()
    {
        var router = new LlmRouter(
            OptionsWith(("corner", Route("fake", "m", null))),
            new Dictionary<string, ILlmProvider> { ["fake"] = new FakeProvider() });

        LlmResult result = await router.CompleteAsync(
            new LlmRequest("corner", "s", "u", RealTimeSchema, "coach_tip"), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Success>().Which.Json.Should().Contain("wider_entry");
    }

    [Fact]
    public async Task Chain_records_cost_and_keeps_breaker_closed()
    {
        var registry = new CircuitBreakerRegistry(new CircuitBreakerOptions(), new FakeTimeProvider());
        var meter = new RecordingCostMeter();
        ILlmProvider chain = LlmProviderChain.Wrap(
            new StubProvider(Success("OK")), meter, registry, NullLogger<CostMeterProvider>.Instance);
        var router = new LlmRouter(
            OptionsWith(("corner", Route("p", "m", null))),
            new Dictionary<string, ILlmProvider> { ["p"] = chain });

        await router.CompleteAsync(new LlmRequest("corner", "s", "u", "{}", "n"), CancellationToken.None);

        meter.Entries.Should().ContainSingle();
        registry.For("p").State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task Open_primary_breaker_downgrades_to_fallback_route()
    {
        var registry = new CircuitBreakerRegistry(new CircuitBreakerOptions(), new FakeTimeProvider());
        Trip(registry.For("p-open"));
        var primary = new StubProvider(Success("PRIMARY"));
        var fallback = new StubProvider(Success("FALLBACK"));
        var providers = new Dictionary<string, ILlmProvider>
        {
            ["p-open"] = new CircuitBreakerProvider(primary, registry),
            ["p-ok"] = new CircuitBreakerProvider(fallback, registry),
        };
        var router = new LlmRouter(
            OptionsWith(("corner", Route("p-open", "m", "lap")), ("lap", Route("p-ok", "m", null))),
            providers);

        LlmResult result = await router.CompleteAsync(new LlmRequest("corner", "s", "u", "{}", "n"), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Success>().Which.Json.Should().Be("FALLBACK");
        primary.CallCount.Should().Be(0);     // never reached — its breaker was open
        fallback.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Open_breaker_without_fallback_returns_circuit_open()
    {
        var registry = new CircuitBreakerRegistry(new CircuitBreakerOptions(), new FakeTimeProvider());
        Trip(registry.For("p-open"));
        var providers = new Dictionary<string, ILlmProvider>
        {
            ["p-open"] = new CircuitBreakerProvider(new StubProvider(Success("PRIMARY")), registry),
        };
        var router = new LlmRouter(OptionsWith(("corner", Route("p-open", "m", null))), providers);

        LlmResult result = await router.CompleteAsync(new LlmRequest("corner", "s", "u", "{}", "n"), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeOfType<LlmFailure.CircuitOpen>();
    }

    [Fact]
    public async Task Timeout_failure_with_fallback_route_falls_back_once_and_yields_a_debrief()
    {
        var primary = new StubProvider(new LlmResult.Failure(new LlmFailure.Timeout("primary timed out")));
        var fallback = new StubProvider(Success("DEBRIEF"));
        var router = new LlmRouter(
            OptionsWith(("debrief", Route("p", "m", "debrief_fallback")), ("debrief_fallback", Route("f", "m", null))),
            new Dictionary<string, ILlmProvider> { ["p"] = primary, ["f"] = fallback });

        LlmResult result = await router.CompleteAsync(new LlmRequest("debrief", "s", "u", "{}", "n"), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Success>().Which.Json.Should().Be("DEBRIEF");
        primary.CallCount.Should().Be(1);
        fallback.CallCount.Should().Be(1);   // single-shot: exactly one fallback hop
    }

    [Fact]
    public async Task ServerError_503_falls_back()
        => (await FallbackResult(new LlmFailure.ServerError("upstream 503", 503)))
            .Should().BeOfType<LlmResult.Success>().Which.Json.Should().Be("FALLBACK");

    [Fact]
    public async Task RateLimited_failure_does_not_fall_back_and_is_returned_as_is()
    {
        // A 429 carries RetryAfter — an immediate same-provider retry can't fix it, so honour the delay, don't fall back.
        var rateLimited = new LlmFailure.RateLimited("slow down", TimeSpan.FromSeconds(5));
        LlmResult result = await FallbackResult(rateLimited, out StubProvider fallback);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeSameAs(rateLimited);
        fallback.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task ServerError_400_does_not_fall_back()
    {
        var badRequest = new LlmFailure.ServerError("bad request", 400);
        LlmResult result = await FallbackResult(badRequest, out StubProvider fallback);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeSameAs(badRequest);
        fallback.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SchemaViolation_does_not_fall_back()
    {
        var violation = new LlmFailure.SchemaViolation("bad shape", "{}");
        LlmResult result = await FallbackResult(violation, out StubProvider fallback);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeSameAs(violation);
        fallback.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Auth_failure_does_not_fall_back()
    {
        var auth = new LlmFailure.Auth("bad key");
        LlmResult result = await FallbackResult(auth, out StubProvider fallback);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeSameAs(auth);
        fallback.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Primary_and_fallback_both_fallback_worthy_returns_the_fallback_error_without_recursing()
    {
        // M22: both hops fail with fallback-worthy errors (primary Timeout, fallback 503). Fallback is
        // single-shot — the router does NOT recurse past the one hop chasing the fallback's own fallback. The
        // returned failure is the fallback provider's error and each provider is hit exactly once.
        var fallbackError = new LlmFailure.ServerError("fallback 503", 503);
        var primary = new StubProvider(new LlmResult.Failure(new LlmFailure.Timeout("primary timed out")));
        var fallback = new StubProvider(new LlmResult.Failure(fallbackError));
        var router = new LlmRouter(
            OptionsWith(("debrief", Route("p", "m", "debrief_fallback")), ("debrief_fallback", Route("f", "m", null))),
            new Dictionary<string, ILlmProvider> { ["p"] = primary, ["f"] = fallback });

        LlmResult result = await router.CompleteAsync(new LlmRequest("debrief", "s", "u", "{}", "n"), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeSameAs(fallbackError);
        primary.CallCount.Should().Be(1);
        fallback.CallCount.Should().Be(1); // single-shot: exactly one fallback hop, no recursion
    }

    [Fact]
    public async Task Fallback_worthy_failure_without_fallback_route_returns_the_original_failure()
    {
        var timeout = new LlmFailure.Timeout("timed out");
        var primary = new StubProvider(new LlmResult.Failure(timeout));
        var router = new LlmRouter(
            OptionsWith(("debrief", Route("p", "m", null))),
            new Dictionary<string, ILlmProvider> { ["p"] = primary });

        LlmResult result = await router.CompleteAsync(new LlmRequest("debrief", "s", "u", "{}", "n"), CancellationToken.None);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeSameAs(timeout);
    }

    private static Task<LlmResult> FallbackResult(LlmFailure primaryError)
        => FallbackResult(primaryError, out _);

    private static Task<LlmResult> FallbackResult(LlmFailure primaryError, out StubProvider fallback)
    {
        var primary = new StubProvider(new LlmResult.Failure(primaryError));
        fallback = new StubProvider(Success("FALLBACK"));
        var router = new LlmRouter(
            OptionsWith(("debrief", Route("p", "m", "debrief_fallback")), ("debrief_fallback", Route("f", "m", null))),
            new Dictionary<string, ILlmProvider> { ["p"] = primary, ["f"] = fallback });

        return router.CompleteAsync(new LlmRequest("debrief", "s", "u", "{}", "n"), CancellationToken.None);
    }

    private static LlmResult Success(string json)
        => new LlmResult.Success(json, new LlmUsage(1, 1), new LlmCallInfo("p", "m", TimeSpan.Zero, "stop"));

    private static void Trip(CircuitBreaker breaker)
    {
        for (int i = 0; i < 3; i++)
        {
            breaker.RecordFailure(new LlmFailure.ServerError("down", 503));
        }
    }

    // Live=true so each route resolves its own provider (these tests exercise the decorator chain + fallback,
    // not the offline redirect).
    private static IOptionsMonitor<LlmOptions> OptionsWith(params (string Key, RouteOptions Route)[] routes)
        => new StaticOptionsMonitor<LlmOptions>(new LlmOptions
        {
            Live = true,
            Routes = routes.ToDictionary(r => r.Key, r => r.Route, StringComparer.Ordinal),
            Providers = new Dictionary<string, ProviderOptions>(),
        });

    private static RouteOptions Route(string providerId, string modelId, string? fallback)
        => new()
        {
            ProviderId = providerId,
            ModelId = modelId,
            MaxOutputTokens = 96,
            Timeout = TimeSpan.FromSeconds(2),
            FallbackRouteKey = fallback,
        };

    private sealed class StubProvider : ILlmProvider
    {
        private readonly LlmResult _result;

        public StubProvider(LlmResult result) => _result = result;

        public int CallCount { get; private set; }

        public Task<LlmResult> CompleteAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(_result);
        }

        public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
            => throw new NotSupportedException();
    }

    private sealed class RecordingCostMeter : ICostMeter
    {
        public List<LlmCostEntry> Entries { get; } = [];

        public Task RecordAsync(LlmCostEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
