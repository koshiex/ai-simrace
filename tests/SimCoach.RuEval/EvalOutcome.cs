namespace SimCoach.RuEval;

/// <summary>
/// The aggregated pass/fail decision for one fixture: the averaged composite, the averaged groundedness (the
/// dimension carrying the hard floor), and the three gate legs. <see cref="Passed"/> requires ALL of: the
/// composite clearing the bar, groundedness clearing its dedicated floor, and EVERY dimension clearing the
/// per-dimension severe-violation floor — so a fluent-but-ungrounded phrase fails on the groundedness floor and a
/// phrase severely bad in any single dimension fails on the per-dimension floor even when its composite clears.
/// </summary>
public sealed record EvalOutcome(
    double Composite,
    double AvgGroundedness,
    bool BarCleared,
    bool FloorCleared,
    bool DimensionFloorsCleared)
{
    public bool Passed => BarCleared && FloorCleared && DimensionFloorsCleared;
}
