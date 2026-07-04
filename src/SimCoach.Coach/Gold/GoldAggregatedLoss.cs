namespace SimCoach.Coach.Gold;

/// <summary>
/// A per-corner loss rolled up across the session for the debrief (B2). <see cref="Corner"/> is the resolved
/// human name (compute carries only <c>corner_id</c>; names stay out of compute per ADR-0010).
/// </summary>
public sealed record GoldAggregatedLoss(
    string Corner,
    int TotalLossMs,
    int AvgLossMs,
    int SampleCount,
    string Reason)
{
    public string CornerNameRu { get; init; } = string.Empty;
}
