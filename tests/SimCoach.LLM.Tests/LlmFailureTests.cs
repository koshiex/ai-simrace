using FluentAssertions;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class LlmFailureTests
{
    [Fact]
    public void Timeout_carries_message()
        => new LlmFailure.Timeout("slow").Message.Should().Be("slow");

    [Fact]
    public void RateLimited_carries_message_and_retry_after()
    {
        var failure = new LlmFailure.RateLimited("429", TimeSpan.FromSeconds(5));

        failure.Message.Should().Be("429");
        failure.RetryAfter.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SchemaViolation_carries_raw_text()
    {
        var failure = new LlmFailure.SchemaViolation("bad json", "{not-json");

        failure.Message.Should().Be("bad json");
        failure.RawText.Should().Be("{not-json");
    }

    [Fact]
    public void Auth_carries_message()
        => new LlmFailure.Auth("401").Message.Should().Be("401");

    [Fact]
    public void ServerError_carries_status_code()
    {
        var failure = new LlmFailure.ServerError("upstream", 502);

        failure.Message.Should().Be("upstream");
        failure.StatusCode.Should().Be(502);
    }

    [Fact]
    public void Transport_carries_message()
        => new LlmFailure.Transport("socket reset").Message.Should().Be("socket reset");

    [Fact]
    public void CircuitOpen_carries_message()
        => new LlmFailure.CircuitOpen("breaker open").Message.Should().Be("breaker open");

    [Fact]
    public void Failure_result_wraps_llm_failure()
    {
        LlmResult result = new LlmResult.Failure(new LlmFailure.Timeout("slow"));

        result.Should().BeOfType<LlmResult.Failure>()
            .Which.Error.Should().BeOfType<LlmFailure.Timeout>();
    }

    [Fact]
    public void Success_result_carries_usage_and_info()
    {
        var info = new LlmCallInfo("openrouter-google", "model-x", TimeSpan.Zero, "stop");
        LlmResult result = new LlmResult.Success("{}", new LlmUsage(10, 5), info);

        LlmResult.Success success = result.Should().BeOfType<LlmResult.Success>().Subject;
        success.Usage.InputTokens.Should().Be(10);
        success.Info.ProviderId.Should().Be("openrouter-google");
    }
}
