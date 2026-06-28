namespace SimCoach.Coach.Gold;

/// <summary>
/// One corner's loss as it appears in a sector/lap <c>top_losses</c> list: the resolved human corner name, the
/// lost milliseconds, and the dominant reason. The compute layer carries only <c>corner_id</c>; the name is
/// resolved here at the Coach layer.
/// </summary>
public sealed record GoldCornerLoss(string Corner, int Ms, string Why);
