namespace SimCoach.Coach.Gold;

/// <summary>
/// The session/debrief Gold payload (FR-060). Fields backed by a no-data sentinel are nullable and dropped by
/// their real precondition, not by <c>has_reference</c>: <see cref="SectorAvgDeltaMs"/> is reference-relative
/// (dropped without a reference); <see cref="TheoreticalBestGapMs"/> is dropped with no clean lap;
/// <see cref="ConsistencyStddevMs"/> is dropped with fewer than two clean laps (its <c>0</c> means "not
/// measurable", not "perfectly consistent"). <see cref="PbTimeMs"/>/<see cref="AverageLapMs"/> drop when not yet
/// known. <see cref="SetupHint"/> has no MVP source (omitted). <see cref="Stints"/> is empty in the MVP.
/// <para>
/// M46 own-optimal (must-fix #2/#4): <see cref="OptimalGapMs"/> is the cross-session, current-session-aware gap
/// to the theoretical best and SUPERSEDES <see cref="TheoreticalBestGapMs"/> — when it is present the builder
/// leaves field-16 <c>null</c> so the LLM sees one number; field-16 is the first-session-only fallback.
/// <see cref="SectorOptimalGapMs"/> is the per-sector deficit vector (≥0) for the debrief deficit ranking. Both
/// are <c>null</c> (dropped) when no persisted optimal exists yet.
/// </para>
/// </summary>
public sealed record GoldSessionPayload(
    int LapCount,
    int CleanLapCount,
    int? PbTimeMs,
    int? AverageLapMs,
    double UndersteerTrend,
    IReadOnlyList<GoldAggregatedLoss> AggregatedLosses,
    IReadOnlyList<int>? SectorAvgDeltaMs,
    double? ConsistencyStddevMs,
    int? TheoreticalBestGapMs,
    int? OptimalGapMs,
    IReadOnlyList<int>? SectorOptimalGapMs,
    string? SetupHint,
    GoldFuelTyreSummary FuelTyre,
    IReadOnlyList<GoldStint> Stints);
