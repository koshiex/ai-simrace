using System.Globalization;
using System.Text.Json.Nodes;
using SimCoach.Coach.Gold;

namespace SimCoach.Coach;

/// <summary>
/// Builds the deterministic session-debrief artifact — the FR-060 fallback emitted when the live debrief LLM
/// is disabled or fails, so the session cadence always yields a real <c>top_losses</c>/<c>top_priority</c>
/// rather than nothing. Same Gold in → same JSON out (no timestamps / randomness); the shape matches
/// <see cref="Schema.OutputSchema.Debrief"/> so downstream validation and persistence treat it exactly like an
/// LLM debrief. Loss channels render to RU via <see cref="ChannelGloss"/> (user-facing text → resx).
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
                ["why"] = Why(loss),
            });
        }

        var artifact = new JsonObject
        {
            ["top_losses"] = topLosses,
            ["top_priority"] = TopPriority(payload.AggregatedLosses),
            ["setup_hint"] = payload.SetupHint is null ? null : JsonValue.Create(payload.SetupHint),
            ["session_metrics"] = SessionMetrics(payload),
        };

        // M46 debrief-only UX: the per-sector deficit ranking rides alongside the metrics, present only when a
        // persisted optimal fed the session (SectorOptimalGapMs non-null). Absent otherwise so first-session /
        // no-optimal debriefs stay byte-identical to before.
        if (payload.SectorOptimalGapMs is not null)
        {
            artifact["sector_deficits"] = SectorDeficits(payload.SectorOptimalGapMs);
        }

        return artifact.ToJsonString();
    }

    // Ranks sectors by how much time the cross-session optimal still holds over this session's best sector
    // (deficit ≥ 0), descending; zero-deficit sectors are omitted (nothing to gain). Sector numbers are
    // 1-based for the reader. Deterministic: ties break by ascending sector.
    private static JsonArray SectorDeficits(IReadOnlyList<int> deficits)
    {
        var ranked = new JsonArray();
        foreach ((int sector, int ms) in deficits
            .Select((ms, i) => (Sector: i + 1, Ms: ms))
            .Where(x => x.Ms > 0)
            .OrderByDescending(x => x.Ms)
            .ThenBy(x => x.Sector))
        {
            ranked.Add(new JsonObject { ["sector"] = sector, ["ms"] = ms });
        }

        return ranked;
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

        // The gap headline supersedes (M46/must-fix #4): the cross-session optimal gap is preferred and the
        // within-session theoretical best is the first-session-only fallback the builder leaves set only when no
        // optimal exists. At most one gap metric renders — the two never appear together.
        if (payload.OptimalGapMs is int optimalGap)
        {
            metrics.Add(Metric("Debrief_Metric_OptimalGap", JsonValue.Create(optimalGap)));
        }
        else if (payload.TheoreticalBestGapMs is int gap)
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

    // The "why" of a per-corner loss is now the M36 dominant CHANNEL, not the retained-but-demoted
    // dominant_reason. The authoritative loss time stays the sibling "ms" (total_loss_ms); dominant_channel_value
    // is a HEURISTIC cross-unit ranking magnitude, so it renders inside an explicitly heuristic-marked phrase and
    // is NEVER surfaced as a bare time beside "ms" (MF-6). With no signed channel (empty), only the neutral gloss
    // renders — the meaningless zero magnitude is dropped.
    private static string Why(GoldAggregatedLoss loss)
    {
        string gloss = ChannelGloss.ToRu(loss.DominantChannel);
        return string.IsNullOrEmpty(loss.DominantChannel)
            ? gloss
            : string.Format(
                CultureInfo.InvariantCulture,
                CoachStrings.Get("Debrief_Why_Channel_Format"),
                gloss,
                loss.DominantChannelValue);
    }

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
            ChannelGloss.ToRu(top.DominantChannel));
    }
}
