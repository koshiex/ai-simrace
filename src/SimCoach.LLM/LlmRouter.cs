using SimCoach.LLM.Providers;

namespace SimCoach.LLM;

/// <summary>
/// Route resolver at the head of the decorator chain (LlmRouter → CircuitBreaker → CostMeter → provider). The
/// supplied provider map is keyed by provider id and already wrapped per <see cref="LlmProviderChain"/>, so the
/// router itself only resolves <see cref="LlmRequest.RouteKey"/> → <see cref="ResolvedRoute"/> → provider and,
/// when the chosen provider's breaker is open and the route declares a <see cref="RouteOptions.FallbackRouteKey"/>,
/// downgrades to the fallback route once. A missing route or unregistered provider is a misconfiguration and
/// throws synchronously (ValidateOnStart makes it unreachable in a composed host).
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
        ILlmProvider provider = ProviderFor(route);
        return CompleteWithFallbackAsync(request, route, provider, ct);
    }

    public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, CancellationToken ct)
        => throw new NotSupportedException("Streaming is declared for P6, not wired in Phase 3.");

    private async Task<LlmResult> CompleteWithFallbackAsync(
        LlmRequest request,
        ResolvedRoute route,
        ILlmProvider provider,
        CancellationToken ct)
    {
        LlmResult result = await provider.CompleteAsync(request, route, ct);

        if (result is LlmResult.Failure { Error: LlmFailure.CircuitOpen }
            && _options.Routes.TryGetValue(request.RouteKey, out RouteOptions? primary)
            && primary.FallbackRouteKey is string fallbackKey)
        {
            ResolvedRoute fallbackRoute = Resolve(fallbackKey);
            ILlmProvider fallbackProvider = ProviderFor(fallbackRoute);
            return await fallbackProvider.CompleteAsync(request, fallbackRoute, ct);
        }

        return result;
    }

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

    private ILlmProvider ProviderFor(ResolvedRoute route)
    {
        if (!_providers.TryGetValue(route.ProviderId, out ILlmProvider? provider))
        {
            throw new InvalidOperationException(
                $"No provider registered for providerId '{route.ProviderId}'.");
        }

        return provider;
    }
}
