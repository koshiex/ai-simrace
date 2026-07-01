namespace SimCoach.LLM;

internal enum CircuitState
{
    Closed,
    Open,
    HalfOpen,
}

/// <summary>
/// One provider's circuit breaker. Closed → (≥<see cref="CircuitBreakerOptions.FailureThreshold"/> trip-worthy
/// failures within <see cref="CircuitBreakerOptions.Window"/>) → Open for
/// <see cref="CircuitBreakerOptions.BreakDuration"/> (or a longer <c>Retry-After</c>) → HalfOpen (one probe) →
/// Closed on success / Open on failure. Only infra failures trip
/// (<see cref="LlmFailure.Timeout"/>/<see cref="LlmFailure.RateLimited"/>/<see cref="LlmFailure.ServerError"/>/
/// <see cref="LlmFailure.Transport"/>); <see cref="LlmFailure.SchemaViolation"/> and <see cref="LlmFailure.Auth"/>
/// are model-quality / config errors, handled elsewhere. Clock-injected (<see cref="TimeProvider"/>) — no sleeps.
/// </summary>
internal sealed class CircuitBreaker
{
    private readonly CircuitBreakerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Queue<DateTimeOffset> _recentFailures = new();

    private CircuitState _state = CircuitState.Closed;
    private DateTimeOffset _openedUntil;
    private bool _probeInFlight;

    public CircuitBreaker(CircuitBreakerOptions options, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _options = options;
        _timeProvider = timeProvider;
    }

    public CircuitState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <summary>True if the call may proceed. Transitions Open→HalfOpen once the break has elapsed and admits a
    /// single probe; further callers are refused until the probe resolves.</summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            switch (_state)
            {
                case CircuitState.Closed:
                    return true;
                case CircuitState.Open:
                    if (now >= _openedUntil)
                    {
                        _state = CircuitState.HalfOpen;
                        _probeInFlight = true;
                        return true;
                    }

                    return false;
                case CircuitState.HalfOpen:
                    if (_probeInFlight)
                    {
                        return false;
                    }

                    _probeInFlight = true;
                    return true;
                default:
                    return false;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _state = CircuitState.Closed;
            _recentFailures.Clear();
            _probeInFlight = false;
            _openedUntil = default;
        }
    }

    /// <summary>Releases a half-open probe that neither succeeded nor failed (the inner call threw, e.g. caller
    /// cancellation) so the breaker can admit the next probe instead of wedging HalfOpen forever. A bare throw is
    /// not provider health, so it neither closes nor reopens the circuit.</summary>
    public void ReleaseProbe()
    {
        lock (_gate)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _probeInFlight = false;
            }
        }
    }

    public void RecordFailure(LlmFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (!IsTripWorthy(failure))
        {
            return;
        }

        lock (_gate)
        {
            DateTimeOffset now = _timeProvider.GetUtcNow();
            TimeSpan breakFor = BreakDurationFor(failure);

            if (_state == CircuitState.HalfOpen)
            {
                Open(now, breakFor);
                return;
            }

            _recentFailures.Enqueue(now);
            DateTimeOffset windowStart = now - _options.Window;
            while (_recentFailures.Count > 0 && _recentFailures.Peek() < windowStart)
            {
                _recentFailures.Dequeue();
            }

            if (_recentFailures.Count >= _options.FailureThreshold)
            {
                Open(now, breakFor);
            }
        }
    }

    private void Open(DateTimeOffset now, TimeSpan breakFor)
    {
        _state = CircuitState.Open;
        _openedUntil = now + breakFor;
        _recentFailures.Clear();
        _probeInFlight = false;
    }

    private TimeSpan BreakDurationFor(LlmFailure failure)
        => failure is LlmFailure.RateLimited { RetryAfter: TimeSpan retryAfter } && retryAfter > _options.BreakDuration
            ? retryAfter
            : _options.BreakDuration;

    // Only infra failures trip. A 4xx (bad request / unknown model / out-of-credits) is a deterministic client
    // error that fallback/retry can't fix and must NOT open the whole provider's breaker, so ServerError trips
    // only at HTTP 5xx.
    private static bool IsTripWorthy(LlmFailure failure)
        => failure is LlmFailure.Timeout
            or LlmFailure.RateLimited
            or LlmFailure.Transport
            or LlmFailure.ServerError { StatusCode: >= 500 };
}
