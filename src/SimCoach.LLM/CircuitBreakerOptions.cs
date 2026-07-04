namespace SimCoach.LLM;

/// <summary>
/// Per-provider circuit-breaker thresholds (FR-037 defaults: 3 trip-worthy failures in 60 s opens for 60 s).
/// All config-driven; bound from <c>Llm:CircuitBreaker</c> at composition (PR-H). Dev-tier (Tier-2) knobs — a
/// resilience/correctness control, not a user-facing slider — so the defaults stand and are documented, not
/// surfaced. Distinct from the router's single-shot <see cref="LlmFailurePolicy"/> (which decides one fallback
/// hop); these govern the breaker's accumulate-to-trip behaviour.
/// </summary>
public sealed record CircuitBreakerOptions
{
    /// <summary>Trip-worthy failures within <see cref="Window"/> that open the breaker.</summary>
    public int FailureThreshold { get; init; } = 3;

    /// <summary>Sliding window over which trip-worthy failures accumulate toward <see cref="FailureThreshold"/>.</summary>
    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long the breaker stays open before admitting a single half-open probe (or longer on a 429's Retry-After).</summary>
    public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(60);

    public void EnsureValid()
    {
        if (FailureThreshold <= 0)
        {
            throw new InvalidOperationException("CircuitBreakerOptions.FailureThreshold must be positive.");
        }

        if (Window <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("CircuitBreakerOptions.Window must be positive.");
        }

        if (BreakDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("CircuitBreakerOptions.BreakDuration must be positive.");
        }
    }
}
