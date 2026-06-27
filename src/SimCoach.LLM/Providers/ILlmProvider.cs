namespace SimCoach.LLM.Providers;

/// <summary>Ring-2 provider seam. PR-F's CircuitBreaker/CostMeter decorators also implement this, so the
/// resolved-knobs handoff is shared across the whole chain.</summary>
internal interface ILlmProvider
{
    Task<LlmResult> CompleteAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct);

    IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct);
}
