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

        if (FallbackRouteKey is not null && string.IsNullOrWhiteSpace(FallbackRouteKey))
        {
            throw new InvalidOperationException("RouteOptions.FallbackRouteKey, when set, must be non-empty.");
        }
    }
}
