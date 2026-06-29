using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimCoach.LLM.Providers;
using SimCoach.Storage.Repositories;

namespace SimCoach.LLM;

/// <summary>
/// Composes the LLM ring: options (monitor-only, so a settings write re-binds without a restart), the
/// circuit-breaker registry, the cost meter + usage/cost-query repositories, one named <c>HttpClient</c> per
/// real provider, the per-provider decorator chains, and the <see cref="LlmRouter"/> as <see cref="ILlmClient"/>.
/// Public so the App composes it and App.Tests can build the same graph without an <c>InternalsVisibleTo</c> hack.
/// The <see cref="ISessionIdProvider"/> the cost meter stamps from is bridged at the App edge (over the
/// producer-owned session context), not here.
/// </summary>
public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Monitor only — NO concrete LlmOptions singleton: a capture-once copy would defeat the settings re-bind.
        services.AddOptions<LlmOptions>()
            .Bind(configuration.GetSection("Llm"))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LlmOptions>, LlmStartupValidator>();

        // The breaker options are a concrete dep of the registry and tuning-stable, so bind eagerly.
        CircuitBreakerOptions breakerOptions =
            configuration.GetSection("Llm:CircuitBreaker").Get<CircuitBreakerOptions>() ?? new CircuitBreakerOptions();
        breakerOptions.EnsureValid();
        services.AddSingleton(breakerOptions);

        services.AddSingleton<ICircuitBreakerRegistry, CircuitBreakerRegistry>();
        services.AddSingleton<ISchemaTranslatorSelector, SchemaTranslatorSelector>();

        services.AddSingleton<LlmUsageRepository>();
        services.AddSingleton<ICostMeter, SqliteCostMeter>();
        services.AddSingleton<ICostQueryRepository, SqliteCostQueryRepository>();

        // Register the HttpClient factory unconditionally (a fake-only config has no named clients but the
        // provider-map factory still resolves IHttpClientFactory), then one named client per real provider.
        services.AddHttpClient();
        LlmOptions bound = configuration.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
        foreach ((string providerId, ProviderOptions provider) in bound.Providers)
        {
            if (string.Equals(providerId, bound.OfflineProviderId, StringComparison.Ordinal))
            {
                continue;
            }

            string authEnvVar = provider.AuthEnvVar;
            // OpenRouterProvider posts a relative "chat/completions", so the base address must end in '/'.
            string baseUrl = provider.BaseUrl.EndsWith('/') ? provider.BaseUrl : provider.BaseUrl + "/";
            services.AddHttpClient(providerId, client => client.BaseAddress = new Uri(baseUrl))
                .AddHttpMessageHandler(() => new BearerAuthHandler(authEnvVar));
        }

        services.AddSingleton<IReadOnlyDictionary<string, ILlmProvider>>(BuildProviderMap);
        services.AddSingleton<ILlmClient>(static sp => new LlmRouter(
            sp.GetRequiredService<IOptionsMonitor<LlmOptions>>(),
            sp.GetRequiredService<IReadOnlyDictionary<string, ILlmProvider>>()));

        return services;
    }

    // Provider id == OfflineProviderId → the network-free FakeProvider; every other id → an OpenRouter adapter
    // over its named HttpClient. Each is wrapped in the breaker→cost-meter chain so an open circuit records no cost.
    private static IReadOnlyDictionary<string, ILlmProvider> BuildProviderMap(IServiceProvider sp)
    {
        LlmOptions options = sp.GetRequiredService<IOptionsMonitor<LlmOptions>>().CurrentValue;
        ICostMeter meter = sp.GetRequiredService<ICostMeter>();
        ICircuitBreakerRegistry breakers = sp.GetRequiredService<ICircuitBreakerRegistry>();
        ILogger<CostMeterProvider> costMeterLogger = sp.GetRequiredService<ILogger<CostMeterProvider>>();
        ISchemaTranslatorSelector selector = sp.GetRequiredService<ISchemaTranslatorSelector>();
        TimeProvider timeProvider = sp.GetRequiredService<TimeProvider>();
        IHttpClientFactory httpFactory = sp.GetRequiredService<IHttpClientFactory>();

        var map = new Dictionary<string, ILlmProvider>(StringComparer.Ordinal);
        foreach ((string providerId, ProviderOptions _) in options.Providers)
        {
            ILlmProvider baseProvider =
                string.Equals(providerId, options.OfflineProviderId, StringComparison.Ordinal)
                    ? new FakeProvider()
                    : new OpenRouterProvider(httpFactory.CreateClient(providerId), selector, timeProvider);
            map[providerId] = LlmProviderChain.Wrap(baseProvider, meter, breakers, costMeterLogger);
        }

        return map;
    }
}
