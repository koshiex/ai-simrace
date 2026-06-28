using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static LlmResult Success(string json)
        => new LlmResult.Success(json, new LlmUsage(1, 1), new LlmCallInfo("p", "m", TimeSpan.Zero, "stop"));

    private static void Trip(CircuitBreaker breaker)
    {
        for (int i = 0; i < 3; i++)
        {
            breaker.RecordFailure(new LlmFailure.ServerError("down", 503));
        }
    }

    private static LlmOptions OptionsWith(params (string Key, RouteOptions Route)[] routes)
        => new()
        {
            Routes = routes.ToDictionary(r => r.Key, r => r.Route, StringComparer.Ordinal),
            Providers = new Dictionary<string, ProviderOptions>(),
        };

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
