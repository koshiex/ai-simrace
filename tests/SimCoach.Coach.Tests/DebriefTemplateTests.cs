using System.Text.Json;
using FluentAssertions;
using SimCoach.Coach.Gold;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class DebriefTemplateTests
{
    [Fact]
    public void BuildJson_is_deterministic()
    {
        GoldArtifact<GoldSessionPayload> gold = Session(Losses(3));
        DebriefTemplate.BuildJson(gold, 5).Should().Be(DebriefTemplate.BuildJson(gold, 5));
    }

    [Fact]
    public void BuildJson_caps_top_losses_at_max()
    {
        string json = DebriefTemplate.BuildJson(Session(Losses(8)), 5);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("top_losses").GetArrayLength().Should().Be(5);
    }

    [Fact]
    public void BuildJson_renders_ru_reason_and_priority()
    {
        GoldArtifact<GoldSessionPayload> gold =
            Session([new GoldAggregatedLoss("Eau Rouge", 600, 120, 5, "early_brake")]);

        string json = DebriefTemplate.BuildJson(gold, 5);

        using var doc = JsonDocument.Parse(json);
        JsonElement firstLoss = doc.RootElement.GetProperty("top_losses")[0];
        firstLoss.GetProperty("corner").GetString().Should().Be("Eau Rouge");
        firstLoss.GetProperty("ms").GetInt32().Should().Be(600);
        firstLoss.GetProperty("why").GetString().Should().Be("раннее торможение");
        doc.RootElement.GetProperty("top_priority").GetString()
            .Should().Contain("Eau Rouge").And.Contain("раннее торможение");
        doc.RootElement.GetProperty("setup_hint").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void BuildJson_with_no_losses_emits_a_non_empty_priority()
    {
        string json = DebriefTemplate.BuildJson(Session([]), 5);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("top_losses").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("top_priority").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void BuildJson_surfaces_session_metrics_with_resx_labels()
    {
        GoldArtifact<GoldSessionPayload> gold = Session([], consistencyStddevMs: 245.5, theoreticalBestGapMs: 380);

        string json = DebriefTemplate.BuildJson(gold, 5);

        using var doc = JsonDocument.Parse(json);
        JsonElement metrics = doc.RootElement.GetProperty("session_metrics");
        metrics.GetArrayLength().Should().Be(2);

        metrics[0].GetProperty("label").GetString().Should().Be(CoachStrings.Get("Debrief_Metric_Consistency"));
        metrics[0].GetProperty("value").GetDouble().Should().Be(245.5);
        metrics[1].GetProperty("label").GetString().Should().Be(CoachStrings.Get("Debrief_Metric_TheoreticalBestGap"));
        metrics[1].GetProperty("value").GetInt32().Should().Be(380);
    }

    [Fact]
    public void BuildJson_drops_session_metrics_that_are_null()
    {
        // <2 clean laps → null consistency; no clean lap → null gap. Both drop, not zero-fill.
        string json = DebriefTemplate.BuildJson(Session([], consistencyStddevMs: null, theoreticalBestGapMs: null), 5);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("session_metrics").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void BuildJson_with_metrics_is_deterministic()
    {
        GoldArtifact<GoldSessionPayload> gold = Session(Losses(3), consistencyStddevMs: 200.5, theoreticalBestGapMs: 120);

        DebriefTemplate.BuildJson(gold, 5).Should().Be(DebriefTemplate.BuildJson(gold, 5));
    }

    private static IReadOnlyList<GoldAggregatedLoss> Losses(int count) =>
    [
        .. Enumerable.Range(0, count)
            .Select(i => new GoldAggregatedLoss($"Corner {i}", 1000 - (i * 50), 100, 3, "low_min_speed")),
    ];

    private static GoldArtifact<GoldSessionPayload> Session(
        IReadOnlyList<GoldAggregatedLoss> losses,
        string? setupHint = null,
        double? consistencyStddevMs = null,
        int? theoreticalBestGapMs = null)
    {
        var payload = new GoldSessionPayload(
            LapCount: 10,
            CleanLapCount: 8,
            PbTimeMs: 90000,
            AverageLapMs: 91000,
            UndersteerTrend: 0.1,
            AggregatedLosses: losses,
            SectorAvgDeltaMs: null,
            ConsistencyStddevMs: consistencyStddevMs,
            TheoreticalBestGapMs: theoreticalBestGapMs,
            SetupHint: setupHint,
            FuelTyre: new GoldFuelTyreSummary(2.5, 0.0),
            Stints: []);
        var header = new GoldSessionBlock("spa", "gt3", "dry-warm", null, HasReference: true);
        return new GoldArtifact<GoldSessionPayload>("gold/1", "session", "ru-RU", header, payload);
    }
}
