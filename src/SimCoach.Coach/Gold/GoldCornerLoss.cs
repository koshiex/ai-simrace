namespace SimCoach.Coach.Gold;

/// <summary>
/// One corner's loss as it appears in a sector/lap <c>top_losses</c> list: the resolved human corner name, the
/// lost milliseconds, and the dominant reason. The compute layer carries only <c>corner_id</c>; the name is
/// resolved here at the Coach layer. <see cref="CornerNameRu"/> is the short Russian display form
/// (<c>CornerNameMap.GetShort</c>) the prompt requires the model to speak; it rides alongside
/// <see cref="Corner"/> as a separate init member so it never disturbs the positional shape.
/// </summary>
public sealed record GoldCornerLoss(string Corner, int Ms, string Why)
{
    public string CornerNameRu { get; init; } = string.Empty;
}
