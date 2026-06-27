namespace SimCoach.LLM;

/// <summary>
/// One provider's connection plus rate card. <see cref="AuthEnvVar"/> names the environment variable
/// holding the API key; the key itself never lives in config or any Gold artifact.
/// </summary>
public sealed record ProviderOptions
{
    public required string BaseUrl { get; init; }

    public required string AuthEnvVar { get; init; }

    public IReadOnlyDictionary<string, ModelRate> Rates { get; init; } =
        new Dictionary<string, ModelRate>();

    public void EnsureValid()
    {
        // Require an absolute http(s) URL — not just Uri.TryCreate(Absolute), which on Unix accepts a
        // bare path like "/api/v1" as a file:// URI (platform-dependent; broke macOS CI).
        if (string.IsNullOrWhiteSpace(BaseUrl)
            || !Uri.TryCreate(BaseUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("ProviderOptions.BaseUrl must be an absolute http(s) URI.");
        }

        if (string.IsNullOrWhiteSpace(AuthEnvVar))
        {
            throw new InvalidOperationException("ProviderOptions.AuthEnvVar must be non-empty.");
        }

        foreach ((string modelId, ModelRate rate) in Rates)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new InvalidOperationException("ProviderOptions.Rates contains an empty model id.");
            }

            rate.EnsureValid();
        }
    }
}
