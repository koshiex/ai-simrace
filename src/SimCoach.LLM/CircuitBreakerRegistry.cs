using System.Collections.Concurrent;

namespace SimCoach.LLM;

internal sealed class CircuitBreakerRegistry : ICircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, CircuitBreaker> _breakers = new(StringComparer.Ordinal);
    private readonly CircuitBreakerOptions _options;
    private readonly TimeProvider _timeProvider;

    public CircuitBreakerRegistry(CircuitBreakerOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
    }

    public CircuitBreaker For(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _breakers.GetOrAdd(providerId, static (_, state) => new CircuitBreaker(state.Options, state.Clock), (Options: _options, Clock: _timeProvider));
    }
}
