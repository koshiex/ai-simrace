namespace SimCoach.Coach;

/// <summary>
/// The single canonical coaching cadence taxonomy (owned by Coach; the LLM library stays cadence-blind).
/// <see cref="Strategy"/> is RESERVED for the deferred pit advisor — no Strategy tip is emitted in the MVP.
/// </summary>
public enum CoachCadence
{
    Corner,
    Sector,
    Lap,
    Session,
    Strategy,
}
