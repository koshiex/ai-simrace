namespace SimCoach.LLM;

/// <summary>One in-memory breaker per provider id, so a failure storm on one upstream (e.g. the real-time
/// <c>openrouter-google</c> route) cannot open another (<c>openrouter-anthropic</c> debrief).</summary>
internal interface ICircuitBreakerRegistry
{
    CircuitBreaker For(string providerId);
}
