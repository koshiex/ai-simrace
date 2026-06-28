using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.LLM;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class CostMeterProviderTests
{
    private static readonly LlmRequest _request = new("corner", "system", "user", "{}", "schema");
    private static readonly ResolvedRoute _route =
        new("openrouter-google", "google/gemini-2.5-flash-lite", 96, TimeSpan.FromSeconds(2), ReasoningEffort.Off, false);

    [Fact]
    public async Task Records_success_entry_from_call_info()
    {
        var meter = new RecordingCostMeter();
        var inner = new StubProvider(new LlmResult.Success(
            "{}", new LlmUsage(120, 18, 40, 5), new LlmCallInfo("openrouter-google", "google/gemini-2.5-flash-lite", TimeSpan.FromMilliseconds(300), "stop")));
        var decorated = new CostMeterProvider(inner, meter, NullLogger<CostMeterProvider>.Instance);

        await decorated.CompleteAsync(_request, _route, CancellationToken.None);

        LlmCostEntry entry = meter.Entries.Should().ContainSingle().Subject;
        entry.ProviderId.Should().Be("openrouter-google");
        entry.ModelId.Should().Be("google/gemini-2.5-flash-lite");
        entry.RouteKey.Should().Be("corner");
        entry.Status.Should().Be("success");
        entry.Usage.CachedInputTokens.Should().Be(40);
    }

    [Fact]
    public async Task Records_failure_entry_from_route_with_zero_usage()
    {
        var meter = new RecordingCostMeter();
        var inner = new StubProvider(new LlmResult.Failure(new LlmFailure.ServerError("down", 503)));
        var decorated = new CostMeterProvider(inner, meter, NullLogger<CostMeterProvider>.Instance);

        await decorated.CompleteAsync(_request, _route, CancellationToken.None);

        LlmCostEntry entry = meter.Entries.Should().ContainSingle().Subject;
        entry.ProviderId.Should().Be("openrouter-google");
        entry.Status.Should().Be("server_error");
        entry.Usage.InputTokens.Should().Be(0);
        entry.Usage.OutputTokens.Should().Be(0);
    }

    [Fact]
    public async Task Cost_write_failure_is_swallowed_and_result_still_returned()
    {
        var meter = new RecordingCostMeter { ThrowOnRecord = new InvalidOperationException("db down") };
        var inner = new StubProvider(new LlmResult.Success(
            "{}", new LlmUsage(1, 1), new LlmCallInfo("openrouter-google", "m", TimeSpan.Zero, "stop")));
        var decorated = new CostMeterProvider(inner, meter, NullLogger<CostMeterProvider>.Instance);

        LlmResult result = await decorated.CompleteAsync(_request, _route, CancellationToken.None);

        result.Should().BeOfType<LlmResult.Success>();
    }

    private sealed class RecordingCostMeter : ICostMeter
    {
        public List<LlmCostEntry> Entries { get; } = [];

        public Exception? ThrowOnRecord { get; init; }

        public Task RecordAsync(LlmCostEntry entry, CancellationToken ct)
        {
            if (ThrowOnRecord is not null)
            {
                throw ThrowOnRecord;
            }

            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class StubProvider : ILlmProvider
    {
        private readonly LlmResult _result;

        public StubProvider(LlmResult result) => _result = result;

        public Task<LlmResult> CompleteAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
            => Task.FromResult(_result);

        public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
