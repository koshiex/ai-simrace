namespace SimCoach.LLM.Providers;

/// <summary>
/// Outermost decorator in the per-call chain (Router → CircuitBreaker → CostMeter → provider). Refuses calls
/// with <see cref="LlmFailure.CircuitOpen"/> while the provider's breaker is open, and records the result so the
/// breaker tracks provider health. Sits OUTSIDE the cost meter, so an open circuit records no cost.
/// </summary>
internal sealed class CircuitBreakerProvider : ILlmProvider
{
    private readonly ILlmProvider _inner;
    private readonly ICircuitBreakerRegistry _registry;

    public CircuitBreakerProvider(ILlmProvider inner, ICircuitBreakerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(registry);
        _inner = inner;
        _registry = registry;
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
    {
        CircuitBreaker breaker = _registry.For(route.ProviderId);
        if (!breaker.TryEnter())
        {
            return new LlmResult.Failure(
                new LlmFailure.CircuitOpen($"Circuit open for provider '{route.ProviderId}'."));
        }

        LlmResult result;
        try
        {
            result = await _inner.CompleteAsync(request, route, ct);
        }
        catch (Exception)
        {
            // The inner call threw rather than returning a Failure (e.g. caller cancellation propagates from
            // OpenRouterProvider). Release any half-open probe so the breaker doesn't wedge, then rethrow.
            breaker.ReleaseProbe();
            throw;
        }

        if (result is LlmResult.Failure failure)
        {
            breaker.RecordFailure(failure.Error);
        }
        else
        {
            breaker.RecordSuccess();
        }

        return result;
    }

    public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
        => _inner.StreamAsync(request, route, ct);
}
