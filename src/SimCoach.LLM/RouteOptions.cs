namespace SimCoach.LLM;

/// <summary>
/// One route's resolution: which provider/model to call and the per-call knobs. The router-only
/// <see cref="FallbackRouteKey"/> is not handed to providers. Scalar ids are <c>required</c> (no
/// meaningful default); the PR-F config binder leaves an unbound member at default rather than throwing,
/// so <see cref="EnsureValid"/> stays the friendly-error surface.
/// </summary>
public sealed record RouteOptions
{
    public required string ProviderId { get; init; }

    public required string ModelId { get; init; }

    public int MaxOutputTokens { get; init; }

    public TimeSpan Timeout { get; init; }

    public ReasoningEffort Reasoning { get; init; } = ReasoningEffort.Off;

    public bool Stream { get; init; }

    /// <summary>When set, marks the system prompt with a provider <c>cache_control</c> breakpoint so a stable
    /// prefix can be served from the provider's prompt cache. Default off — metering-prep only, so an unset route
    /// keeps the current uncached behaviour on the wire.</summary>
    public bool CacheSystemPrompt { get; init; }

    /// <summary>Sampling temperature. Null leaves it to the provider default; 0 = deterministic (preferred for
    /// short structured coaching output). Emitted to the provider only when set.</summary>
    public double? Temperature { get; init; }

    /// <summary>Nucleus-sampling cutoff. Null leaves it to the provider default; 1.0 = no truncation. Emitted to
    /// the provider only when set. Best practice tunes temperature xor top_p, not both.</summary>
    public double? TopP { get; init; }

    public string? FallbackRouteKey { get; init; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(ProviderId))
        {
            throw new InvalidOperationException("RouteOptions.ProviderId must be non-empty.");
        }

        if (string.IsNullOrWhiteSpace(ModelId))
        {
            throw new InvalidOperationException("RouteOptions.ModelId must be non-empty.");
        }

        if (MaxOutputTokens <= 0)
        {
            throw new InvalidOperationException("RouteOptions.MaxOutputTokens must be positive.");
        }

        if (Timeout < TimeSpan.FromMilliseconds(100))
        {
            throw new InvalidOperationException("RouteOptions.Timeout must be at least 100 ms.");
        }

        if (Temperature is < 0d or > 2d)
        {
            throw new InvalidOperationException("RouteOptions.Temperature, when set, must be within [0, 2].");
        }

        if (TopP is < 0d or > 1d)
        {
            throw new InvalidOperationException("RouteOptions.TopP, when set, must be within [0, 1].");
        }

        if (FallbackRouteKey is not null && string.IsNullOrWhiteSpace(FallbackRouteKey))
        {
            throw new InvalidOperationException("RouteOptions.FallbackRouteKey, when set, must be non-empty.");
        }
    }
}
