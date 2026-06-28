using Microsoft.Extensions.Logging;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Builds one provider id's decorator chain: CircuitBreaker (outermost) → CostMeter → base provider. PR-H's DI
/// calls this per registered provider and hands the resulting map to <see cref="LlmRouter"/>, so the router stays
/// a pure resolver. Order is load-bearing: the cost meter sits inside the breaker, so an open circuit records no
/// cost.
/// </summary>
internal static class LlmProviderChain
{
    public static ILlmProvider Wrap(
        ILlmProvider baseProvider,
        ICostMeter meter,
        ICircuitBreakerRegistry breakers,
        ILogger<CostMeterProvider> costMeterLogger)
    {
        ArgumentNullException.ThrowIfNull(baseProvider);
        var metered = new CostMeterProvider(baseProvider, meter, costMeterLogger);
        return new CircuitBreakerProvider(metered, breakers);
    }
}
