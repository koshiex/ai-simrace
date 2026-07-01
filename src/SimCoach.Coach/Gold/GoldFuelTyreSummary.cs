namespace SimCoach.Coach.Gold;

/// <summary>
/// The derived session-level fuel/tyre summary (FR-060): averaged fuel-per-lap and end-of-session tyre wear.
/// These are derived Gold-tier scalars — the raw per-frame fuel/wear channels never leave the machine.
/// On ACC the wear channel is an honest zero (the sim reports no tyre wear).
/// </summary>
public sealed record GoldFuelTyreSummary(double AvgFuelPerLapL, double EndTyreWearPct);
