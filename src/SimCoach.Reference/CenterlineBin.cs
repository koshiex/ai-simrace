namespace SimCoach.Reference;

/// <summary>
/// One 1-metre distance bin of the aggregate corridor centerline: the median world position and
/// median absolute lateral load over every lap that passed through this bin. See ADR-0014.
/// </summary>
public sealed record CenterlineBin
{
    /// <summary>Metre index along the lap, 0..lapLength.</summary>
    public required int DistanceM { get; init; }

    /// <summary>Median world-frame X (metres) over the contributing laps.</summary>
    public required float X { get; init; }

    /// <summary>Median world-frame Z (metres) over the contributing laps.</summary>
    public required float Z { get; init; }

    /// <summary>Median absolute lateral g over the contributing laps (0 when g-force is absent).</summary>
    public required float LateralG { get; init; }

    /// <summary>
    /// Number of laps that contributed a real sample to this bin. Zero marks a carry-filled gap
    /// (the position is copied from the previous real bin), never trusted for geometry.
    /// </summary>
    public required int LapSamples { get; init; }
}
