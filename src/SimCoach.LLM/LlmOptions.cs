namespace SimCoach.LLM;

/// <summary>
/// Root LLM configuration: the route table (RouteKey → provider/model/knobs) and the provider table
/// (providerId → base url/auth/rate card). <see cref="Live"/> is the single fake-vs-real switch read by the
/// router (off by default → every route resolves to the <see cref="OfflineProviderId"/>/<see cref="OfflineModelId"/>
/// pair while keeping the route's other knobs). Cross-collection checks (rate coverage incl. the offline pair,
/// route/cadence completeness, fallback acyclicity) are PR-F/PR-H's <c>ValidateOnStart</c>;
/// <see cref="EnsureValid"/> here is structural validity only.
/// </summary>
public sealed record LlmOptions
{
    public bool Live { get; init; }

    /// <summary>Provider used for every route while <see cref="Live"/> is false (network-free FakeProvider).</summary>
    public string OfflineProviderId { get; init; } = "fake";

    /// <summary>Model id paired with <see cref="OfflineProviderId"/> while <see cref="Live"/> is false.</summary>
    public string OfflineModelId { get; init; } = "fake/local";

    public IReadOnlyDictionary<string, RouteOptions> Routes { get; init; } =
        new Dictionary<string, RouteOptions>();

    public IReadOnlyDictionary<string, ProviderOptions> Providers { get; init; } =
        new Dictionary<string, ProviderOptions>();

    public void EnsureValid()
    {
        if (Routes.Count == 0)
        {
            throw new InvalidOperationException("LlmOptions.Routes must contain at least one route.");
        }

        if (Providers.Count == 0)
        {
            throw new InvalidOperationException("LlmOptions.Providers must contain at least one provider.");
        }

        if (string.IsNullOrWhiteSpace(OfflineProviderId))
        {
            throw new InvalidOperationException("LlmOptions.OfflineProviderId must be non-empty.");
        }

        if (string.IsNullOrWhiteSpace(OfflineModelId))
        {
            throw new InvalidOperationException("LlmOptions.OfflineModelId must be non-empty.");
        }

        foreach ((string key, RouteOptions route) in Routes)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("LlmOptions.Routes contains an empty route key.");
            }

            route.EnsureValid();
        }

        foreach ((string key, ProviderOptions provider) in Providers)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException("LlmOptions.Providers contains an empty provider id.");
            }

            provider.EnsureValid();
        }
    }
}
