using FluentAssertions;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class CostCalculatorTests
{
    private static readonly ModelRate _rate = new()
    {
        InputPerMillion = 3m,
        OutputPerMillion = 15m,
        CachedInputPerMillion = 0.3m,
    };

    [Fact]
    public void Computes_decimal_cost_with_cached_and_reasoning_tokens()
    {
        // InputTokens (1000) is inclusive of cached (200) → 800 billed at input rate.
        var usage = new LlmUsage(InputTokens: 1000, OutputTokens: 500, CachedInputTokens: 200, ReasoningTokens: 100);

        decimal cost = CostCalculator.Compute(_rate, usage);

        // 800/1e6*3 + 200/1e6*0.3 + (500+100)/1e6*15
        cost.Should().Be(0.0024m + 0.00006m + 0.009m);
    }

    [Fact]
    public void Reasoning_tokens_bill_at_the_output_rate()
    {
        var withoutReasoning = new LlmUsage(1000, 500);
        var withReasoning = new LlmUsage(1000, 500, ReasoningTokens: 100);

        decimal delta = CostCalculator.Compute(_rate, withReasoning) - CostCalculator.Compute(_rate, withoutReasoning);

        delta.Should().Be(100 / 1_000_000m * _rate.OutputPerMillion);
    }

    [Fact]
    public void Zero_usage_costs_zero()
        => CostCalculator.Compute(_rate, new LlmUsage(0, 0)).Should().Be(0m);
}
