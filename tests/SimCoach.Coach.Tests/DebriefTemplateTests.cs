using System.Text.Json;
using System.Text.Json.Nodes;
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
    public void BuildJson_surfaces_only_the_present_metric_in_the_partial_case()
    {
        // Partial case exercising the two independent `if` branches' ordering: <2 clean laps drops
        // consistency, but a clean lap is present so the gap survives → a length-1 array holding only the
        // gap (not a leading null slot, and the gap keeps its slot despite the earlier branch dropping).
        string json = DebriefTemplate.BuildJson(Session([], consistencyStddevMs: null, theoreticalBestGapMs: 380), 5);

        using var doc = JsonDocument.Parse(json);
        JsonElement metrics = doc.RootElement.GetProperty("session_metrics");
        metrics.GetArrayLength().Should().Be(1);
        metrics[0].GetProperty("label").GetString().Should().Be(CoachStrings.Get("Debrief_Metric_TheoreticalBestGap"));
        metrics[0].GetProperty("value").GetInt32().Should().Be(380);
    }

    [Fact]
    public void BuildJson_session_metrics_is_a_byte_stable_golden()
    {
        GoldArtifact<GoldSessionPayload> gold = Session(Losses(3), consistencyStddevMs: 200.5, theoreticalBestGapMs: 120);

        string json = DebriefTemplate.BuildJson(gold, 5);

        // Golden pin (replaces the former self-equality determinism check): the exact serialized bytes of the
        // session_metrics array — field order (label before value), array order (consistency before gap) and
        // numeric formatting. Labels are drawn from the same resx the template uses, escaped by the same
        // default encoder, so this stays a byte-for-byte match without hard-coding Cyrillic escape sequences.
        string consistencyLabel = JsonValue.Create(CoachStrings.Get("Debrief_Metric_Consistency"))!.ToJsonString();
        string gapLabel = JsonValue.Create(CoachStrings.Get("Debrief_Metric_TheoreticalBestGap"))!.ToJsonString();
        string expected = $"[{{\"label\":{consistencyLabel},\"value\":200.5}},{{\"label\":{gapLabel},\"value\":120}}]";

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("session_metrics").GetRawText().Should().Be(expected);
    }

    [Fact]
    public void BuildJson_renders_the_optimal_gap_headline_and_ranked_sector_deficits()
    {
        // A persisted optimal fed the session: the optimal gap is THE gap metric (theoretical-best absent) and
        // the per-sector deficits render as a descending ranking with zero-deficit sectors omitted.
        GoldArtifact<GoldSessionPayload> gold = Session(
            [], consistencyStddevMs: 200.5, optimalGapMs: 1044, sectorOptimalGapMs: [120, 0, 884]);

        string json = DebriefTemplate.BuildJson(gold, 5);

        using var doc = JsonDocument.Parse(json);
        JsonElement metrics = doc.RootElement.GetProperty("session_metrics");
        metrics.GetArrayLength().Should().Be(2);
        metrics[1].GetProperty("label").GetString().Should().Be(CoachStrings.Get("Debrief_Metric_OptimalGap"));
        metrics[1].GetProperty("value").GetInt32().Should().Be(1044);

        JsonElement deficits = doc.RootElement.GetProperty("sector_deficits");
        deficits.GetArrayLength().Should().Be(2, "the zero-deficit sector is omitted from the ranking");
        deficits[0].GetProperty("sector").GetInt32().Should().Be(3);
        deficits[0].GetProperty("ms").GetInt32().Should().Be(884);
        deficits[1].GetProperty("sector").GetInt32().Should().Be(1);
        deficits[1].GetProperty("ms").GetInt32().Should().Be(120);
    }

    [Fact]
    public void BuildJson_omits_sector_deficits_when_no_optimal_fed_the_session()
    {
        string json = DebriefTemplate.BuildJson(Session([], theoreticalBestGapMs: 380), 5);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("sector_deficits", out _).Should().BeFalse();
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
        int? theoreticalBestGapMs = null,
        int? optimalGapMs = null,
        IReadOnlyList<int>? sectorOptimalGapMs = null)
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
            OptimalGapMs: optimalGapMs,
            SectorOptimalGapMs: sectorOptimalGapMs,
            SetupHint: setupHint,
            FuelTyre: new GoldFuelTyreSummary(2.5, 0.0),
            Stints: []);
        var header = new GoldSessionBlock("spa", "gt3", "dry-warm", null, HasReference: true);
        return new GoldArtifact<GoldSessionPayload>("gold/1", "session", "ru-RU", header, payload);
    }
}
