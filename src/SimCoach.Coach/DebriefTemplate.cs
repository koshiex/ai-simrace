using System.Globalization;
using System.Text.Json.Nodes;
using SimCoach.Coach.Gold;

namespace SimCoach.Coach;

/// <summary>
/// Builds the deterministic session-debrief artifact — the FR-060 fallback emitted when the live debrief LLM
/// is disabled or fails, so the session cadence always yields a real <c>top_losses</c>/<c>top_priority</c>
/// rather than nothing. Same Gold in → same JSON out (no timestamps / randomness); the shape matches
/// <see cref="Schema.OutputSchema.Debrief"/> so downstream validation and persistence treat it exactly like an
/// LLM debrief. Loss reasons render to RU via <see cref="CoachStrings"/> (user-facing text → resx).
/// </summary>
public static class DebriefTemplate
{
    public static string BuildJson(GoldArtifact<GoldSessionPayload> gold, int maxLosses)
    {
        ArgumentNullException.ThrowIfNull(gold);
        GoldSessionPayload payload = gold.Event;

        var topLosses = new JsonArray();
        foreach (GoldAggregatedLoss loss in payload.AggregatedLosses.Take(maxLosses))
        {
            topLosses.Add(new JsonObject
            {
                ["corner"] = loss.Corner,
                ["ms"] = loss.TotalLossMs,
                ["why"] = ReasonGloss.ToRu(loss.Reason),
            });
        }

        var artifact = new JsonObject
        {
            ["top_losses"] = topLosses,
            ["top_priority"] = TopPriority(payload.AggregatedLosses),
            ["setup_hint"] = payload.SetupHint is null ? null : JsonValue.Create(payload.SetupHint),
            ["session_metrics"] = SessionMetrics(payload),
        };

        return artifact.ToJsonString();
    }

    // Grounded session metrics (M20): consistency stddev + theoretical-best gap surfaced with neutral RU resx
    // labels. Both are null-dropped on their own precondition upstream (fewer than two clean laps → no
    // consistency; no clean lap → no gap), so a null value contributes no entry rather than a misleading zero.
    // Fixed order (consistency, then gap) keeps the artifact byte-stable for the golden test.
    private static JsonArray SessionMetrics(GoldSessionPayload payload)
    {
        var metrics = new JsonArray();
        if (payload.ConsistencyStddevMs is double consistency)
        {
            metrics.Add(Metric("Debrief_Metric_Consistency", JsonValue.Create(consistency)));
        }

        if (payload.TheoreticalBestGapMs is int gap)
        {
            metrics.Add(Metric("Debrief_Metric_TheoreticalBestGap", JsonValue.Create(gap)));
        }

        return metrics;
    }

    private static JsonObject Metric(string labelKey, JsonNode value) => new()
    {
        ["label"] = CoachStrings.Get(labelKey),
        ["value"] = value,
    };

    private static string TopPriority(IReadOnlyList<GoldAggregatedLoss> losses)
    {
        if (losses.Count == 0)
        {
            return CoachStrings.Get("Debrief_TopPriority_None");
        }

        GoldAggregatedLoss top = losses[0];
        return string.Format(
            CultureInfo.InvariantCulture,
            CoachStrings.Get("Debrief_TopPriority_Format"),
            top.Corner,
            ReasonGloss.ToRu(top.Reason));
    }
}
