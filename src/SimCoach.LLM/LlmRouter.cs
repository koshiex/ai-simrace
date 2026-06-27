using SimCoach.LLM.Providers;

namespace SimCoach.LLM;

/// <summary>
/// Trivial route resolver: RouteKey → <see cref="ResolvedRoute"/> → keyed provider. PR-F replaces this
/// with a LlmRouter → CircuitBreaker → CostMeter → provider decorator chain; the <see cref="ILlmClient"/>
/// surface is unchanged. A missing route or unregistered provider is a misconfiguration and throws
/// (fail-fast), never an <see cref="LlmResult.Failure"/> — PR-F's <c>ValidateOnStart</c> makes it
/// unreachable in a composed host.
/// </summary>
internal sealed class LlmRouter : ILlmClient
{
    private readonly LlmOptions _options;
    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;

    public LlmRouter(LlmOptions options, IReadOnlyDictionary<string, ILlmProvider> providers)
    {
        _options = options;
        _providers = providers;
    }

    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        ResolvedRoute route = Resolve(request.RouteKey);
        if (!_providers.TryGetValue(route.ProviderId, out ILlmProvider? provider))
        {
            throw new InvalidOperationException(
                $"No provider registered for providerId '{route.ProviderId}' (route '{request.RouteKey}').");
        }

        return provider.CompleteAsync(request, route, ct);
    }

    public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, CancellationToken ct)
        => throw new NotSupportedException("Streaming is declared for P6, not wired in Phase 3.");

    private ResolvedRoute Resolve(string routeKey)
    {
        if (!_options.Routes.TryGetValue(routeKey, out RouteOptions? route))
        {
            throw new InvalidOperationException($"No route configured for RouteKey '{routeKey}'.");
        }

        return new ResolvedRoute(
            route.ProviderId,
            route.ModelId,
            route.MaxOutputTokens,
            route.Timeout,
            route.Reasoning,
            route.Stream);
    }
}
