namespace SimCoach.Reference;

/// <summary>
/// The aggregate corridor centerline for a track: one smooth path built by taking the MEDIAN world
/// position per 1-metre bin across many wrap-segmented laps (aggregate position first, ADR-0014).
/// This is the geometry source the corner detector differentiates exactly once.
/// </summary>
public sealed record MedianCenterline
{
    /// <summary>Normalized track id (e.g. "monza").</summary>
    public required string TrackId { get; init; }

    /// <summary>Lap length in metres the bins are laid out against.</summary>
    public required float LapLengthM { get; init; }

    /// <summary>
    /// Number of laps that contributed at least one binned sample. Aggregation is only trustworthy
    /// at <see cref="MedianCenterlineBuilder.MinLapsForTrust"/> or more (the median needs enough laps
    /// to reject single-lap outliers — see ADR-0014 / T7).
    /// </summary>
    public required int LapCount { get; init; }

    /// <summary>Bins in ascending <see cref="CenterlineBin.DistanceM"/> order.</summary>
    public required IReadOnlyList<CenterlineBin> Bins { get; init; }
}
