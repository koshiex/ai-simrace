using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SimCoach.LLM;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;

namespace SimCoach.RuEval;

/// <summary>
/// Stands up the real LLM ring for the gate (must-fix f): the public <see cref="LlmServiceCollectionExtensions.AddLlm"/>
/// graph over a committed in-memory <c>appsettings</c> with <c>Llm:Live=true</c>, plus the two deps AddLlm does
/// not register itself — a THROWAWAY SQLite connection (so <c>SqliteCostMeter</c>/<c>ICostQueryRepository</c>/
/// <c>LlmUsageRepository</c> resolve) and a stub <see cref="ISessionIdProvider"/> (fixed id). Live candidate
/// routes (Gemini) drive generation; the new <c>ru_judge</c> route points at <c>anthropic/claude-sonnet-4.6</c>.
/// Only constructed inside the env-gated path — never on the offline lane.
/// </summary>
public sealed class RuEvalGraph : IDisposable
{
    private const string HermeticJudgeRoute = "ru_judge";

    private readonly ServiceProvider _provider;
    private readonly string _dbPath;

    public RuEvalGraph(string judgeRouteKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(judgeRouteKey);

        _dbPath = Path.Combine(Path.GetTempPath(), "simcoach-rueval-" + Guid.NewGuid().ToString("N") + ".db");
        var factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = _dbPath });
        new DatabaseMigrator(factory).Migrate();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(factory);
        services.AddSingleton<ISessionIdProvider>(new StubSessionIds("ru-eval"));

        IConfiguration config = new ConfigurationBuilder().AddInMemoryCollection(Config(judgeRouteKey)).Build();
        services.AddLlm(config);
        _provider = services.BuildServiceProvider();
    }

    public ILlmClient Client => _provider.GetRequiredService<ILlmClient>();

    /// <summary>Read side over the <c>llm_usage</c> ledger the meter writes on the hot path (M30 cost tabulation).</summary>
    public ICostQueryRepository CostQuery => _provider.GetRequiredService<ICostQueryRepository>();

    /// <summary>
    /// Resolves a candidate route key to its configured model id off the SAME route table the graph builds from
    /// — no DI graph, SQLite, or network needed, so the always-on hermetic A/B tests (M30) can assert distinct
    /// candidate models on the offline lane without constructing the throwaway ring.
    /// </summary>
    public static string ModelIdFor(string routeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);
        return Config(HermeticJudgeRoute).TryGetValue($"Llm:Routes:{routeKey}:ModelId", out string? modelId)
                && !string.IsNullOrWhiteSpace(modelId)
            ? modelId
            : throw new InvalidOperationException($"No model id configured for route '{routeKey}'.");
    }

    public void Dispose()
    {
        _provider.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    // Live route table + provider rate cards. The judge route is anthropic/claude-sonnet-4.6; candidate routes
    // mirror the App's coach cadences (corner→gemini, debrief→anthropic). The offline pair keeps a rate so
    // AddLlm's startup validator is satisfied even with Live=true.
    private static Dictionary<string, string?> Config(string judgeRouteKey) => new()
    {
        ["Llm:Live"] = "true",
        ["Llm:OfflineProviderId"] = "fake",
        ["Llm:OfflineModelId"] = "fake/local",

        ["Llm:Routes:corner:ProviderId"] = "openrouter-google",
        ["Llm:Routes:corner:ModelId"] = "google/gemini-3.1-flash-lite",
        ["Llm:Routes:corner:MaxOutputTokens"] = "96",
        ["Llm:Routes:corner:Timeout"] = "00:00:05",
        ["Llm:Routes:corner:Temperature"] = "0",
        ["Llm:Routes:sector:ProviderId"] = "openrouter-google",
        ["Llm:Routes:sector:ModelId"] = "google/gemini-2.5-flash-lite",
        ["Llm:Routes:sector:MaxOutputTokens"] = "192",
        ["Llm:Routes:sector:Timeout"] = "00:00:05",
        ["Llm:Routes:lap:ProviderId"] = "openrouter-google",
        ["Llm:Routes:lap:ModelId"] = "google/gemini-2.5-flash-lite",
        ["Llm:Routes:lap:MaxOutputTokens"] = "192",
        ["Llm:Routes:lap:Timeout"] = "00:00:05",
        ["Llm:Routes:debrief:ProviderId"] = "openrouter-anthropic",
        ["Llm:Routes:debrief:ModelId"] = "anthropic/claude-sonnet-4.6",
        ["Llm:Routes:debrief:MaxOutputTokens"] = "2000",
        ["Llm:Routes:debrief:Timeout"] = "00:00:30",
        ["Llm:Routes:debrief:Reasoning"] = "Low",
        ["Llm:Routes:strategy:ProviderId"] = "openrouter-google",
        ["Llm:Routes:strategy:ModelId"] = "google/gemini-2.5-flash-lite",
        ["Llm:Routes:strategy:MaxOutputTokens"] = "192",
        ["Llm:Routes:strategy:Timeout"] = "00:00:05",

        // M30 A/B candidate routes: identical knobs, model id is the ONLY variable so the shadow-harness
        // isolates the gemini-2.5 vs gemini-3.1 one-liner quality/cost trade. Budget/timeout are sized for the
        // widest fixture (the debrief), so any cadence's request can fan through either route unchanged.
        ["Llm:Routes:ab_gemini_25:ProviderId"] = "openrouter-google",
        ["Llm:Routes:ab_gemini_25:ModelId"] = "google/gemini-2.5-flash-lite",
        ["Llm:Routes:ab_gemini_25:MaxOutputTokens"] = "2000",
        ["Llm:Routes:ab_gemini_25:Timeout"] = "00:00:30",
        ["Llm:Routes:ab_gemini_25:Temperature"] = "0",
        ["Llm:Routes:ab_gemini_31:ProviderId"] = "openrouter-google",
        ["Llm:Routes:ab_gemini_31:ModelId"] = "google/gemini-3.1-flash-lite",
        ["Llm:Routes:ab_gemini_31:MaxOutputTokens"] = "2000",
        ["Llm:Routes:ab_gemini_31:Timeout"] = "00:00:30",
        ["Llm:Routes:ab_gemini_31:Temperature"] = "0",

        [$"Llm:Routes:{judgeRouteKey}:ProviderId"] = "openrouter-anthropic",
        [$"Llm:Routes:{judgeRouteKey}:ModelId"] = "anthropic/claude-sonnet-4.6",
        [$"Llm:Routes:{judgeRouteKey}:MaxOutputTokens"] = "600",
        [$"Llm:Routes:{judgeRouteKey}:Timeout"] = "00:00:30",
        [$"Llm:Routes:{judgeRouteKey}:Reasoning"] = "Low",

        ["Llm:Providers:openrouter-google:BaseUrl"] = "https://openrouter.ai/api/v1/",
        ["Llm:Providers:openrouter-google:AuthEnvVar"] = "OPENROUTER_API_KEY",
        ["Llm:Providers:openrouter-google:Rates:google/gemini-2.5-flash-lite:InputPerMillion"] = "0.10",
        ["Llm:Providers:openrouter-google:Rates:google/gemini-2.5-flash-lite:OutputPerMillion"] = "0.40",
        ["Llm:Providers:openrouter-google:Rates:google/gemini-2.5-flash-lite:CachedInputPerMillion"] = "0.05",
        ["Llm:Providers:openrouter-google:Rates:google/gemini-3.1-flash-lite:InputPerMillion"] = "0.25",
        ["Llm:Providers:openrouter-google:Rates:google/gemini-3.1-flash-lite:OutputPerMillion"] = "1.50",
        ["Llm:Providers:openrouter-google:Rates:google/gemini-3.1-flash-lite:CachedInputPerMillion"] = "0.125",

        ["Llm:Providers:openrouter-anthropic:BaseUrl"] = "https://openrouter.ai/api/v1/",
        ["Llm:Providers:openrouter-anthropic:AuthEnvVar"] = "OPENROUTER_API_KEY",
        ["Llm:Providers:openrouter-anthropic:Rates:anthropic/claude-sonnet-4.6:InputPerMillion"] = "3.00",
        ["Llm:Providers:openrouter-anthropic:Rates:anthropic/claude-sonnet-4.6:OutputPerMillion"] = "15.00",
        ["Llm:Providers:openrouter-anthropic:Rates:anthropic/claude-sonnet-4.6:CachedInputPerMillion"] = "0.30",

        ["Llm:Providers:fake:BaseUrl"] = "https://fake.local/",
        ["Llm:Providers:fake:AuthEnvVar"] = "SIMCOACH_FAKE_UNUSED",
        ["Llm:Providers:fake:Rates:fake/local:InputPerMillion"] = "0",
        ["Llm:Providers:fake:Rates:fake/local:OutputPerMillion"] = "0",
        ["Llm:Providers:fake:Rates:fake/local:CachedInputPerMillion"] = "0",
    };

    private sealed class StubSessionIds(string id) : ISessionIdProvider
    {
        public string? CurrentSessionId { get; } = id;
    }
}
