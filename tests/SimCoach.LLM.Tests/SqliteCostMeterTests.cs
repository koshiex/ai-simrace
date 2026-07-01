using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SimCoach.LLM;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class SqliteCostMeterTests : IDisposable
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), "simcoach-tests", Path.GetRandomFileName(), "simcoach.db");

    private readonly SqliteConnectionFactory _factory;

    public SqliteCostMeterTests()
    {
        _factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = _dbPath });
        new DatabaseMigrator(_factory).Migrate();
        // llm_usage.session_id is an FK to sessions(id); the cost meter now stamps it, so seed the session.
        new SessionRepository(_factory).Insert(new SessionRow
        {
            Id = "sess-1",
            StartedAtUtc = new DateTimeOffset(2026, 6, 28, 12, 0, 0, TimeSpan.Zero),
            Sim = "acc",
            TrackId = "spa",
            CarId = "ferrari_296_gt3",
            WeatherBucket = "dry-warm",
            McapPath = "/recordings/sess-1",
        });
    }

    [Fact]
    public async Task Records_row_with_provider_cached_column_and_config_priced_cost()
    {
        SqliteCostMeter meter = Meter();

        await meter.RecordAsync(
            new LlmCostEntry(
                "openrouter-google",
                "google/gemini-2.5-flash-lite",
                "corner",
                new LlmUsage(1000, 500, CachedInputTokens: 200),
                TimeSpan.FromMilliseconds(250),
                "success"),
            CancellationToken.None);

        using SqliteConnection connection = _factory.Create();
        connection.QuerySingle<string>("SELECT session_id FROM llm_usage").Should().Be("sess-1");
        connection.QuerySingle<string>("SELECT provider FROM llm_usage").Should().Be("openrouter-google");
        connection.QuerySingle<int>("SELECT cached_input_tokens FROM llm_usage").Should().Be(200);
        connection.QuerySingle<string>("SELECT cadence FROM llm_usage").Should().Be("corner");
        connection.QuerySingle<string>("SELECT status FROM llm_usage").Should().Be("success");
        // 800/1e6*0.1 + 200/1e6*0.05 + 500/1e6*0.4 = 0.00029
        double cost = connection.QuerySingle<double>("SELECT cost_usd FROM llm_usage");
        cost.Should().BeApproximately(0.00029, 1e-9);
    }

    [Fact]
    public async Task Failure_entry_records_zero_cost_and_its_status()
    {
        SqliteCostMeter meter = Meter();

        await meter.RecordAsync(
            new LlmCostEntry(
                "openrouter-google",
                "google/gemini-2.5-flash-lite",
                "corner",
                new LlmUsage(0, 0),
                TimeSpan.Zero,
                "timeout"),
            CancellationToken.None);

        using SqliteConnection connection = _factory.Create();
        connection.QuerySingle<double>("SELECT cost_usd FROM llm_usage").Should().Be(0d);
        connection.QuerySingle<string>("SELECT status FROM llm_usage").Should().Be("timeout");
    }

    private SqliteCostMeter Meter()
    {
        IOptions<LlmOptions> options = Options.Create(new LlmOptions
        {
            Routes = new Dictionary<string, RouteOptions>
            {
                ["corner"] = new()
                {
                    ProviderId = "openrouter-google",
                    ModelId = "google/gemini-2.5-flash-lite",
                    MaxOutputTokens = 96,
                    Timeout = TimeSpan.FromSeconds(2),
                },
            },
            Providers = new Dictionary<string, ProviderOptions>
            {
                ["openrouter-google"] = new()
                {
                    BaseUrl = "https://openrouter.test/api/v1/",
                    AuthEnvVar = "OPENROUTER_API_KEY",
                    Rates = new Dictionary<string, ModelRate>
                    {
                        ["google/gemini-2.5-flash-lite"] = new()
                        {
                            InputPerMillion = 0.1m,
                            OutputPerMillion = 0.4m,
                            CachedInputPerMillion = 0.05m,
                        },
                    },
                },
            },
        });

        return new SqliteCostMeter(
            new LlmUsageRepository(_factory), options, new StubSessionIds("sess-1"), TimeProvider.System);
    }

    private sealed class StubSessionIds(string? id) : ISessionIdProvider
    {
        public string? CurrentSessionId { get; } = id;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        string? directory = Path.GetDirectoryName(_dbPath);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
