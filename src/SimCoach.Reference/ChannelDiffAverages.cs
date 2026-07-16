namespace SimCoach.Reference;

/// <summary>
/// A corner's per-channel diagnostic diffs aggregated abs-then-average over its lossy-corner samples
/// (ADR-0020, decision 1 + 6). Each value is <c>mean(|per_corner_diff|)</c> over the
/// <c>DeltaMs &gt; 0</c> conditioned set — the same set <see cref="SessionLossAccumulator"/> rolls the
/// losses up from. A report-only diagnostic magnitude, never a time; the authoritative loss is
/// <c>total_loss_ms</c>.
/// </summary>
internal readonly record struct ChannelDiffAverages(
    float BrakePointDiffM,
    float ThrottleResumeDiffM,
    float MinSpeedDiffKmh,
    float LineDeviationM);
