namespace SimCoach.RuEval;

/// <summary>
/// The aggregated pass/fail decision for one fixture: the averaged composite, the averaged groundedness (the
/// dimension carrying the hard floor), and the two gate legs. <see cref="Passed"/> requires BOTH the composite
/// clearing the bar and groundedness clearing the floor — a fluent-but-ungrounded phrase fails on the floor.
/// </summary>
public sealed record EvalOutcome(
    double Composite,
    double AvgGroundedness,
    bool BarCleared,
    bool FloorCleared)
{
    public bool Passed => BarCleared && FloorCleared;
}
