using System.Net.Http.Headers;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Adds <c>Authorization: Bearer &lt;key&gt;</c> from the environment variable named by the provider's
/// <see cref="ProviderOptions.AuthEnvVar"/>, read at send time (so a late-set key works). The key never lives
/// in config or any Gold artifact. Only enters the pipeline when a real provider is wired (PR-H, <c>Llm:Live</c>);
/// reaching <see cref="SendAsync"/> means a live call is in flight, so a missing key is a hard config error.
/// </summary>
internal sealed class BearerAuthHandler : DelegatingHandler
{
    private readonly string _authEnvVar;

    public BearerAuthHandler(string authEnvVar)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authEnvVar);
        _authEnvVar = authEnvVar;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string? key = Environment.GetEnvironmentVariable(_authEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"API key environment variable '{_authEnvVar}' is unset; cannot authorize a live LLM call.");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return base.SendAsync(request, cancellationToken);
    }
}
