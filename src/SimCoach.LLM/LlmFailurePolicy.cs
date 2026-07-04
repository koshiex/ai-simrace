namespace SimCoach.LLM;

/// <summary>
/// The router's single-shot fallback predicate — deliberately distinct from and narrower than the circuit
/// breaker's <c>CircuitBreaker.IsTripWorthy</c> (accumulate-to-trip) set. The router falls back exactly once on
/// <see cref="LlmFailure.Timeout"/> / <see cref="LlmFailure.Transport"/> /
/// <see cref="LlmFailure.ServerError"/>{StatusCode &gt;= 500} / <see cref="LlmFailure.CircuitOpen"/> — a class of
/// failures a second provider can plausibly fix.
/// <para>
/// <see cref="LlmFailure.RateLimited"/> is EXCLUDED even though the breaker counts it: a <c>429</c> carries a
/// <c>RetryAfter</c> and an immediate retry on the same provider cannot fix it, so the correct behaviour is to
/// honour that delay rather than fall back. <see cref="LlmFailure.SchemaViolation"/> (model-quality, Coach
/// retry/template) and <see cref="LlmFailure.Auth"/> (bad key) are non-fallback by construction — a cheaper
/// fallback model won't fix either. Keeping this predicate in its own one-type file stops the two policies from
/// silently drifting into each other.
/// </para>
/// </summary>
internal static class LlmFailurePolicy
{
    public static bool ShouldRouterFallback(LlmFailure failure)
        => failure is LlmFailure.Timeout
            or LlmFailure.Transport
            or LlmFailure.CircuitOpen
            or LlmFailure.ServerError { StatusCode: >= 500 };
}
