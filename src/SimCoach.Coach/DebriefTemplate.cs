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
                ["why"] = ReasonRu(loss.Reason),
            });
        }

        var artifact = new JsonObject
        {
            ["top_losses"] = topLosses,
            ["top_priority"] = TopPriority(payload.AggregatedLosses),
            ["setup_hint"] = payload.SetupHint is null ? null : JsonValue.Create(payload.SetupHint),
        };

        return artifact.ToJsonString();
    }

    private static string TopPriority(IReadOnlyList<GoldAggregatedLoss> losses)
    {
        if (losses.Count == 0)
        {
            return CoachStrings.Get("Debrief_TopPriority_None");
        }

        GoldAggregatedLoss top = losses[0];
        return string.Format(
            CultureInfo.InvariantCulture, CoachStrings.Get("Debrief_TopPriority_Format"), top.Corner, ReasonRu(top.Reason));
    }

    private static string ReasonRu(string reason) =>
        string.IsNullOrEmpty(reason) ? CoachStrings.Get("Reason_slower") : CoachStrings.Get("Reason_" + reason);
}
