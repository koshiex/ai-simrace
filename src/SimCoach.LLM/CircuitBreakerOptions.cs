namespace SimCoach.LLM;

/// <summary>
/// Per-provider circuit-breaker thresholds (FR-037 defaults: 3 trip-worthy failures in 60 s opens for 60 s).
/// All config-driven; bound from <c>Llm:CircuitBreaker</c> at composition (PR-H).
/// </summary>
public sealed record CircuitBreakerOptions
{
    public int FailureThreshold { get; init; } = 3;

    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(60);

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
