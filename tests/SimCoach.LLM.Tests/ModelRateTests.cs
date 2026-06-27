using FluentAssertions;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class ModelRateTests
{
    [Fact]
    public void Positive_rates_pass()
        => new ModelRate { InputPerMillion = 3m, OutputPerMillion = 15m, CachedInputPerMillion = 0.3m }
            .Invoking(r => r.EnsureValid()).Should().NotThrow();

    [Fact]
    public void Zero_rates_allowed()
        => new ModelRate().Invoking(r => r.EnsureValid()).Should().NotThrow();

    [Fact]
    public void Negative_input_rate_throws()
        => new ModelRate { InputPerMillion = -1m }.Invoking(r => r.EnsureValid())
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Negative_cached_rate_throws()
        => new ModelRate { CachedInputPerMillion = -0.1m }.Invoking(r => r.EnsureValid())
            .Should().Throw<InvalidOperationException>();
}
