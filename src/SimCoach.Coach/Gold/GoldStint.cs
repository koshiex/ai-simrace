namespace SimCoach.Coach.Gold;

/// <summary>
/// A stint summary in the debrief payload. Declared for completeness; the stint list is empty in the MVP
/// (race-craft is a later phase).
/// </summary>
public sealed record GoldStint(
    int StartLap,
    int EndLap,
    int TyreCompound,
    double TyreDegradationPct,
    int AvgLapMs);
