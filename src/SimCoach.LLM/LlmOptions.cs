namespace SimCoach.LLM;

/// <summary>
/// Root LLM configuration: the route table (RouteKey → provider/model/knobs) and the provider table
/// (providerId → base url/auth/rate card). <see cref="Live"/> flips the router to real providers (off by
/// default). Cross-collection checks (rate coverage, route/cadence completeness, fallback acyclicity) are
/// PR-F's <c>ValidateOnStart</c>; <see cref="EnsureValid"/> here is structural validity only.
/// </summary>
public sealed record LlmOptions
{
    public bool Live { get; init; }

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
