using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class CircuitBreakerTests
{
    [Fact]
    public void Closed_breaker_allows_entry()
    {
        var clock = new FakeTimeProvider();
        CircuitBreaker breaker = Breaker(clock);

        breaker.TryEnter().Should().BeTrue();
        breaker.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void Trips_after_threshold_infra_failures_in_window()
    {
        var clock = new FakeTimeProvider();
        CircuitBreaker breaker = Breaker(clock);

        for (int i = 0; i < 3; i++)
        {
            breaker.RecordFailure(new LlmFailure.ServerError("down", 503));
        }

        breaker.State.Should().Be(CircuitState.Open);
        breaker.TryEnter().Should().BeFalse();
    }

    [Fact]
    public void Failures_spread_beyond_window_do_not_trip()
    {
        var clock = new FakeTimeProvider();
        CircuitBreaker breaker = Breaker(clock);

        breaker.RecordFailure(new LlmFailure.Transport("blip"));
        clock.Advance(TimeSpan.FromSeconds(61));
        breaker.RecordFailure(new LlmFailure.Transport("blip"));
        clock.Advance(TimeSpan.FromSeconds(61));
        breaker.RecordFailure(new LlmFailure.Transport("blip"));

        breaker.State.Should().Be(CircuitState.Closed);
        breaker.TryEnter().Should().BeTrue();
    }

    [Fact]
    public void Schema_violation_and_auth_do_not_trip()
    {
        var clock = new FakeTimeProvider();
        CircuitBreaker breaker = Breaker(clock);

        for (int i = 0; i < 5; i++)
        {
            breaker.RecordFailure(new LlmFailure.SchemaViolation("bad json", "{"));
            breaker.RecordFailure(new LlmFailure.Auth("no key"));
        }

        breaker.State.Should().Be(CircuitState.Closed);
        breaker.TryEnter().Should().BeTrue();
    }

    [Fact]
    public void Opens_then_admits_single_probe_after_break()
    {
        var clock = new FakeTimeProvider();
        CircuitBreaker breaker = Breaker(clock);
        Trip(breaker);

        breaker.TryEnter().Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(60));

        breaker.TryEnter().Should().BeTrue();      // probe admitted
        breaker.State.Should().Be(CircuitState.HalfOpen);
        breaker.TryEnter().Should().BeFalse();      // only one probe in flight
    }

    [Fact]
    public void Half_open_success_closes_the_circuit()
    {
        var clock = new FakeTimeProvider();
        CircuitBreaker breaker = Breaker(clock);
        Trip(breaker);
        clock.Advance(TimeSpan.FromSeconds(60));
        breaker.TryEnter();

        breaker.RecordSuccess();

        breaker.State.Should().Be(CircuitState.Closed);
        breaker.TryEnter().Should().BeTrue();
    }

    [Fact]
    public void Half_open_failure_reopens_the_circuit()
    {
        var clock = new FakeTimeProvider();
        CircuitBreaker breaker = Breaker(clock);
        Trip(breaker);
        clock.Advance(TimeSpan.FromSeconds(60));
        breaker.TryEnter();

        breaker.RecordFailure(new LlmFailure.ServerError("still down", 503));

        breaker.State.Should().Be(CircuitState.Open);
        breaker.TryEnter().Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(60));
        breaker.TryEnter().Should().BeTrue();
    }

    [Fact]
    public void Retry_after_longer_than_break_extends_open_period()
    {
        var clock = new FakeTimeProvider();
        CircuitBreaker breaker = Breaker(clock);

        for (int i = 0; i < 3; i++)
        {
            breaker.RecordFailure(new LlmFailure.RateLimited("slow down", TimeSpan.FromSeconds(120)));
        }

        clock.Advance(TimeSpan.FromSeconds(61));
        breaker.TryEnter().Should().BeFalse();   // 60 s break elapsed but Retry-After=120 s still holds
        clock.Advance(TimeSpan.FromSeconds(60));
        breaker.TryEnter().Should().BeTrue();
    }

    [Fact]
    public void Registry_isolates_breakers_per_provider()
    {
        var clock = new FakeTimeProvider();
        var registry = new CircuitBreakerRegistry(new CircuitBreakerOptions(), clock);

        Trip(registry.For("openrouter-google"));

        registry.For("openrouter-google").TryEnter().Should().BeFalse();
        registry.For("openrouter-anthropic").TryEnter().Should().BeTrue();
        registry.For("openrouter-anthropic").State.Should().Be(CircuitState.Closed);
    }

    private static CircuitBreaker Breaker(FakeTimeProvider clock)
        => new(
            new CircuitBreakerOptions
            {
                FailureThreshold = 3,
                Window = TimeSpan.FromSeconds(60),
                BreakDuration = TimeSpan.FromSeconds(60),
            },
            clock);

    private static void Trip(CircuitBreaker breaker)
    {
        for (int i = 0; i < 3; i++)
        {
            breaker.RecordFailure(new LlmFailure.ServerError("down", 503));
        }
    }
}
