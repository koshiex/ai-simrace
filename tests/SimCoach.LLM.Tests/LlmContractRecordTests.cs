using FluentAssertions;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class LlmContractRecordTests
{
    [Fact]
    public void Usage_defaults_cached_and_reasoning_to_zero()
    {
        var usage = new LlmUsage(100, 40);

        usage.CachedInputTokens.Should().Be(0);
        usage.ReasoningTokens.Should().Be(0);
    }

    [Fact]
    public void Delta_terminal_when_finish_reason_present()
    {
        new LlmDelta("chunk", null).FinishReason.Should().BeNull();
        new LlmDelta(string.Empty, "stop").FinishReason.Should().Be("stop");
    }

    [Fact]
    public void Request_has_value_equality()
    {
        var a = new LlmRequest("corner", "sys", "user", "{}", "coach_tip");
        var b = new LlmRequest("corner", "sys", "user", "{}", "coach_tip");

        b.Should().Be(a);
    }
}
