using Microsoft.Extensions.Options;
using SimCoach.LLM.Providers;

namespace SimCoach.LLM;

/// <summary>
/// Route resolver at the head of the decorator chain (LlmRouter → CircuitBreaker → CostMeter → provider). The
/// supplied provider map is keyed by provider id and already wrapped per <see cref="LlmProviderChain"/>, so the
/// router itself only resolves <see cref="LlmRequest.RouteKey"/> → <see cref="ResolvedRoute"/> → provider and,
/// when the primary fails with a router-fallback-worthy error (<see cref="LlmFailurePolicy.ShouldRouterFallback"/>)
/// and the route declares a <see cref="RouteOptions.FallbackRouteKey"/>, downgrades to the fallback route once.
/// A missing route or unregistered provider is a misconfiguration and throws synchronously (ValidateOnStart makes
/// it unreachable in a composed host).
/// <para>
/// Options are read from <see cref="IOptionsMonitor{T}.CurrentValue"/> per resolve so a settings write
/// (model swap, <c>Llm:Live</c> flip, see <c>SqliteSettingsConfigurationSource</c>) takes effect on the next
/// call without a restart. While <see cref="LlmOptions.Live"/> is false every route resolves to the configured
/// offline provider/model pair, keeping the route's timeout/tokens/reasoning — so replay/CI produce real
/// (zero-cost) <c>llm_usage</c> rows with no API key.
/// </para>
/// </summary>
internal sealed class LlmRouter : ILlmClient
{
    private readonly IOptionsMonitor<LlmOptions> _options;
    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;

    public LlmRouter(IOptionsMonitor<LlmOptions> options, IReadOnlyDictionary<string, ILlmProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(providers);
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

        if (result is LlmResult.Failure failure
            && LlmFailurePolicy.ShouldRouterFallback(failure.Error)
            && _options.CurrentValue.Routes.TryGetValue(request.RouteKey, out RouteOptions? primary)
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
        LlmOptions options = _options.CurrentValue;
        if (!options.Routes.TryGetValue(routeKey, out RouteOptions? route))
        {
            throw new InvalidOperationException($"No route configured for RouteKey '{routeKey}'.");
        }

        // Offline: keep the route's call knobs but swap provider+model to the network-free pair. Live: the
        // route's own provider/model. The fake-vs-real decision lives here, not in any caller.
        (string providerId, string modelId) = options.Live
            ? (route.ProviderId, route.ModelId)
            : (options.OfflineProviderId, options.OfflineModelId);

        return new ResolvedRoute(
            providerId,
            modelId,
            route.MaxOutputTokens,
            route.Timeout,
            route.Reasoning,
            route.Stream,
            route.Temperature,
            route.TopP);
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
