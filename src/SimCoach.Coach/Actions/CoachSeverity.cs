namespace SimCoach.Coach.Actions;

/// <summary>
/// The coarse display band the overlay chip renders ("corner · high"). Derived deterministically from a
/// <see cref="CoachPriority"/> via <see cref="CoachOptions.SeverityFor"/> (config <c>SeverityBands</c>) — a
/// separate projection from the total-order <see cref="CoachPriority.Rank"/>, so ordering stays integer-keyed
/// while the chip has a stable, config-driven source.
/// </summary>
public enum CoachSeverity
{
    Low,
    Medium,
    High,
}
