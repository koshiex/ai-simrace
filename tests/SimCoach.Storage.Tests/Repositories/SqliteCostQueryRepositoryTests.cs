using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class SqliteCostQueryRepositoryTests : RepositoryTestBase
{
    private static readonly DateTimeOffset _now = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly LlmUsageRepository _writer;
    private readonly SessionRepository _sessions;
    private readonly SqliteCostQueryRepository _query;

    public SqliteCostQueryRepositoryTests()
    {
        _writer = new LlmUsageRepository(Factory);
        _sessions = new SessionRepository(Factory);
        _query = new SqliteCostQueryRepository(Factory, new FakeTimeProvider(_now));
    }

    [Fact]
    public async Task GetSessionCost_sums_only_the_requested_session()
    {
        SeedSession("s1");
        SeedSession("s2");
        Seed("s1", _now, "corner", "openrouter-google", "gemini", 0.001);
        Seed("s1", _now, "corner", "openrouter-google", "gemini", 0.002);
        Seed("s2", _now, "corner", "openrouter-google", "gemini", 0.005);

        CostSummary summary = await _query.GetSessionCostAsync("s1", CancellationToken.None);

        summary.CallCount.Should().Be(2);
        summary.CostUsd.Should().BeApproximately(0.003, 1e-9);
    }

    [Fact]
    public async Task GetRolling30Day_excludes_rows_older_than_the_window()
    {
        Seed(null, _now.AddDays(-10), "corner", "openrouter-google", "gemini", 0.002);
        Seed(null, _now.AddDays(-40), "corner", "openrouter-google", "gemini", 0.009);

        RollingCost rolling = await _query.GetRolling30DayCostAsync(CancellationToken.None);

        rolling.CallCount.Should().Be(1);
        rolling.CostUsd.Should().BeApproximately(0.002, 1e-9);
    }

    [Fact]
    public async Task GetCostByDay_groups_by_iso_date()
    {
        Seed(null, _now, "corner", "openrouter-google", "gemini", 0.001);
        Seed(null, _now, "corner", "openrouter-google", "gemini", 0.001);
        Seed(null, _now.AddDays(-1), "corner", "openrouter-google", "gemini", 0.004);

        IReadOnlyList<CostByDay> byDay = await _query.GetCostByDayAsync(7, CancellationToken.None);

        byDay.Should().HaveCount(2);
        byDay[0].Day.Should().Be("2026-06-27");
        byDay[0].CostUsd.Should().BeApproximately(0.004, 1e-9);
        byDay[1].Day.Should().Be("2026-06-28");
        byDay[1].CallCount.Should().Be(2);
    }

    [Fact]
    public async Task GetCostByRoute_groups_and_orders_by_cost_desc()
    {
        Seed(null, _now, "corner", "openrouter-google", "gemini", 0.001);
        Seed(null, _now, "corner", "openrouter-google", "gemini", 0.001);
        Seed(null, _now, "debrief", "openrouter-anthropic", "sonnet", 0.020);

        IReadOnlyList<CostByRoute> byRoute = await _query.GetCostByRouteAsync(_now.AddDays(-1), CancellationToken.None);

        byRoute.Should().HaveCount(2);
        byRoute[0].RouteKey.Should().Be("debrief");
        byRoute[0].ProviderId.Should().Be("openrouter-anthropic");
        byRoute[0].CostUsd.Should().BeApproximately(0.020, 1e-9);
        byRoute[1].RouteKey.Should().Be("corner");
        byRoute[1].CallCount.Should().Be(2);
    }

    private void SeedSession(string id)
        => _sessions.Insert(new SessionRow
        {
            Id = id,
            StartedAtUtc = _now,
            Sim = "acc",
            TrackId = "spa",
            CarId = "gt3",
            WeatherBucket = "dry",
            McapPath = $"{id}.mcap",
        });

    private void Seed(
        string? session,
        DateTimeOffset ts,
        string cadence,
        string provider,
        string model,
        double cost)
        => _writer.Insert(new LlmUsageRow
        {
            SessionId = session,
            TsUtc = ts,
            ModelId = model,
            Provider = provider,
            Cadence = cadence,
            InputTokens = 100,
            OutputTokens = 20,
            CachedInputTokens = 0,
            CostUsd = cost,
            LatencyMs = 100,
            Status = "success",
        });
}
