namespace SimCoach.Reference;

/// <summary>
/// The own-optimal ("theoretical best") reference for one triple (M46): the per-sector best clean
/// DURATIONS stitched into a synthetic target lap faster than any single lap driven. TIME ONLY — the
/// control channels are never modelled here (three laps glued at seams would fabricate line/brake advice),
/// so consumers read <see cref="SectorDurationsMs"/> / <see cref="TargetLapTimeMs"/> and nothing else.
/// </summary>
public sealed record OptimalReference
{
    /// <summary>Per-sector best durations (ms) in sector order, each surviving the outlier guard.</summary>
    public required IReadOnlyList<int> SectorDurationsMs { get; init; }

    /// <summary>The target lap time = Σ <see cref="SectorDurationsMs"/>.</summary>
    public required int TargetLapTimeMs { get; init; }

    /// <summary>Which stored clean lap each sector best came from (provenance, one per sector).</summary>
    public required IReadOnlyList<SectorBestSource> Sources { get; init; }
}

/// <summary>Provenance of a single per-sector best: the clean lap that set it.</summary>
public sealed record SectorBestSource
{
    public required int SectorIndex { get; init; }
    public required int DurationMs { get; init; }
    public required string SessionId { get; init; }
    public required int LapNumber { get; init; }
}
