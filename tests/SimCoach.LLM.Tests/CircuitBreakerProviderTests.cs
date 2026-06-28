using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using SimCoach.LLM;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class CircuitBreakerProviderTests
{
    private static readonly LlmRequest _request = new("corner", "system", "user", "{}", "schema");
    private static readonly ResolvedRoute _route =
        new("openrouter-google", "google/gemini-2.5-flash-lite", 96, TimeSpan.FromSeconds(2), ReasoningEffort.Off, false);

    [Fact]
    public async Task Open_circuit_short_circuits_without_calling_inner()
    {
        var clock = new FakeTimeProvider();
        var registry = new CircuitBreakerRegistry(new CircuitBreakerOptions(), clock);
        CircuitBreaker breaker = registry.For(_route.ProviderId);
        for (int i = 0; i < 3; i++)
        {
            breaker.RecordFailure(new LlmFailure.ServerError("down", 503));
        }

        var inner = new StubProvider(Success());
        var decorated = new CircuitBreakerProvider(inner, registry);

        LlmResult result = await decorated.CompleteAsync(_request, _route, CancellationToken.None);

        result.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeOfType<LlmFailure.CircuitOpen>();
        inner.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Success_passes_through_and_keeps_circuit_closed()
    {
        var clock = new FakeTimeProvider();
        var registry = new CircuitBreakerRegistry(new CircuitBreakerOptions(), clock);
        var inner = new StubProvider(Success());
        var decorated = new CircuitBreakerProvider(inner, registry);

        LlmResult result = await decorated.CompleteAsync(_request, _route, CancellationToken.None);

        result.Should().BeOfType<LlmResult.Success>();
        inner.CallCount.Should().Be(1);
        registry.For(_route.ProviderId).State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task Repeated_failures_open_the_circuit_and_then_stop_calling_inner()
    {
        var clock = new FakeTimeProvider();
        var registry = new CircuitBreakerRegistry(new CircuitBreakerOptions(), clock);
        var inner = new StubProvider(new LlmResult.Failure(new LlmFailure.ServerError("down", 503)));
        var decorated = new CircuitBreakerProvider(inner, registry);

        for (int i = 0; i < 3; i++)
        {
            await decorated.CompleteAsync(_request, _route, CancellationToken.None);
        }

        LlmResult fourth = await decorated.CompleteAsync(_request, _route, CancellationToken.None);

        inner.CallCount.Should().Be(3);
        fourth.Should().BeOfType<LlmResult.Failure>().Which.Error.Should().BeOfType<LlmFailure.CircuitOpen>();
    }

    private static LlmResult Success()
        => new LlmResult.Success("{}", new LlmUsage(1, 1), new LlmCallInfo("openrouter-google", "m", TimeSpan.Zero, "stop"));

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
}
