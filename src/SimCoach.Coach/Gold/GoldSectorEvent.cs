namespace SimCoach.Coach.Gold;

/// <summary>
/// The sector-cadence Gold payload. <see cref="SectorTimeMs"/> is the always-present absolute sector time;
/// <see cref="DeltaMs"/> is reference-relative (omitted without a reference). <see cref="TopCorner"/> is the
/// resolved human name of the biggest-loss corner (empty when there are no losses).
/// </summary>
public sealed record GoldSectorEvent(
    int SectorIdx,
    int SectorTimeMs,
    int? DeltaMs,
    string TopCorner,
    IReadOnlyList<GoldCornerLoss> TopLosses);
