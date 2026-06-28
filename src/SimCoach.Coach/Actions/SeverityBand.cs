namespace SimCoach.Coach.Actions;

/// <summary>
/// One threshold in the <see cref="CoachPriority"/> → <see cref="CoachSeverity"/> projection: every priority
/// up to and including <see cref="MaxInclusive"/> (and above the previous band) maps to <see cref="Band"/>.
/// Bands are ordered ascending by <see cref="MaxInclusive"/>; the last band is a catch-all so every priority
/// resolves. Keyed on the <see cref="CoachPriority"/> order itself, not a bare rank int.
/// </summary>
public readonly record struct SeverityBand(CoachPriority MaxInclusive, CoachSeverity Band);
