namespace SimCoach.Coach.Gold;

/// <summary>
/// A per-corner loss rolled up across the session for the debrief (B2). <see cref="Corner"/> is the resolved
/// human name (compute carries only <c>corner_id</c>; names stay out of compute per ADR-0010).
/// <see cref="Reason"/> (proto <c>dominant_reason</c>, field 5) is RETAINED for back-compat but no longer
/// authoritative — the debrief renders <see cref="DominantChannel"/> instead (M36). <see cref="DominantChannelValue"/>
/// is a HEURISTIC scaled ranking magnitude, never an additive time; it must never be summed with
/// <see cref="TotalLossMs"/> (MF-6).
/// </summary>
public sealed record GoldAggregatedLoss(
    string Corner,
    int TotalLossMs,
    int AvgLossMs,
    int SampleCount,
    string Reason)
{
    public string CornerNameRu { get; init; } = string.Empty;

    public string DominantChannel { get; init; } = string.Empty;

    public int DominantChannelValue { get; init; }
}
