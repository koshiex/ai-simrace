namespace SimCoach.Coach.Gold;

/// <summary>
/// The lap-cadence Gold payload. <see cref="LapTimeMs"/> is the always-present absolute lap time;
/// <see cref="DeltaMs"/> is reference-relative (omitted without a reference). The <see cref="IsPb"/>/
/// <see cref="IsClean"/> bools and the <see cref="Thermal"/> overheat flags are always populated so the
/// fail-closed clause evaluator can read them.
/// </summary>
public sealed record GoldLapEvent(
    int LapNumber,
    int LapTimeMs,
    int? DeltaMs,
    bool IsPb,
    bool IsClean,
    string TopCorner,
    GoldThermalSummary Thermal,
    IReadOnlyList<GoldCornerLoss> TopLosses);
