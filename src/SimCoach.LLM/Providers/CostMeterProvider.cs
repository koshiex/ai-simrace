using Microsoft.Extensions.Logging;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Inner decorator (Router → CircuitBreaker → CostMeter → provider). Records every call's usage/cost off the
/// hot path; success rows price the real usage, failure rows record zero cost with the failure kind as status
/// (so call-count dashboards see failures too). A cost-write error is logged and swallowed — it must never
/// break a tip.
/// </summary>
internal sealed class CostMeterProvider : ILlmProvider
{
    private readonly ILlmProvider _inner;
    private readonly ICostMeter _meter;
    private readonly ILogger<CostMeterProvider> _logger;

    public CostMeterProvider(ILlmProvider inner, ICostMeter meter, ILogger<CostMeterProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(meter);
        ArgumentNullException.ThrowIfNull(logger);
        _inner = inner;
        _meter = meter;
        _logger = logger;
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
    {
        LlmResult result = await _inner.CompleteAsync(request, route, ct);

        try
        {
            LlmCostEntry entry = result switch
            {
                LlmResult.Success success => new LlmCostEntry(
                    success.Info.ProviderId,
                    success.Info.ProviderModelId,
                    request.RouteKey,
                    success.Usage,
                    success.Info.Latency,
                    "success"),
                LlmResult.Failure failure => new LlmCostEntry(
                    route.ProviderId,
                    route.ModelId,
                    request.RouteKey,
                    new LlmUsage(0, 0),
                    TimeSpan.Zero,
                    StatusFor(failure.Error)),
                _ => throw new InvalidOperationException("Unknown LlmResult variant."),
            };

            await _meter.RecordAsync(entry, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record LLM cost for route {RouteKey}.", request.RouteKey);
        }

        return result;
    }

    public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
        => _inner.StreamAsync(request, route, ct);

    private static string StatusFor(LlmFailure failure)
        => failure switch
        {
            LlmFailure.Timeout => "timeout",
            LlmFailure.RateLimited => "rate_limited",
            LlmFailure.ServerError => "server_error",
            LlmFailure.Transport => "transport",
            LlmFailure.Auth => "auth",
            LlmFailure.SchemaViolation => "schema_violation",
            LlmFailure.CircuitOpen => "circuit_open",
            _ => "failure",
        };
}
