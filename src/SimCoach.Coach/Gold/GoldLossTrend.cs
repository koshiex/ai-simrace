namespace SimCoach.Coach.Gold;

/// <summary>
/// One point of a per-corner lap-indexed loss series (M41, proto <c>LossTrend</c> on <c>AggregatedLoss</c>
/// field 12): this corner's loss on a given lap, positive = slower than reference. A magnitude series that
/// enables a "getting worse / improving" per-corner read; <see cref="LossMs"/> is never summed into
/// <see cref="GoldAggregatedLoss.TotalLossMs"/>. Rides <see cref="GoldAggregatedLoss"/> (per-corner), not
/// <see cref="GoldSessionPayload"/>.
/// </summary>
public sealed record GoldLossTrend(int LapNumber, int LossMs);
