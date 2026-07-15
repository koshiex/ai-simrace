namespace SimCoach.Storage.Repositories;

/// <summary>
/// One stored clean lap's per-sector durations plus its provenance and total lap time — the raw material
/// from which the own-optimal reference (M46) selects a per-sector best. <see cref="LapTimeMs"/> is kept
/// alongside <see cref="SectorTimesMs"/> so the builder can run its cheap <c>Σ sectors ≈ lap_time</c>
/// sanity filter without a second read; <see cref="SessionId"/>/<see cref="LapNumber"/> carry the
/// provenance of whichever sector best is chosen.
/// </summary>
public sealed record CleanLapSectors
{
    public required string SessionId { get; init; }
    public required int LapNumber { get; init; }
    public required int LapTimeMs { get; init; }

    /// <summary>Per-sector durations (ms) in sector order; all present (the query drops laps with a null sector).</summary>
    public required IReadOnlyList<int> SectorTimesMs { get; init; }
}
