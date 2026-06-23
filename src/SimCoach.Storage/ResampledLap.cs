namespace SimCoach.Storage;

/// <summary>
/// One lap resampled onto a fixed 1 m position grid — the flat column set written to
/// <c>laps.parquet</c> (and reused for reference encoding in a later PR). Every array has
/// <see cref="GridLength"/> entries, index <c>k</c> being the sample at <c>k</c> metres round the lap.
/// Tyre temps are split into the four wheel columns [FL, FR, RL, RR].
/// </summary>
public sealed record ResampledLap
{
    public required int LapNumber { get; init; }
    public required int GridLength { get; init; }
    public required float[] PositionNormalized { get; init; }
    public required int[] TMsFromLapStart { get; init; }
    public required float[] SpeedMps { get; init; }
    public required float[] ThrottlePct { get; init; }
    public required float[] BrakePct { get; init; }
    public required float[] SteerRad { get; init; }
    public required int[] Gear { get; init; }
    public required float[] TyreTempFl { get; init; }
    public required float[] TyreTempFr { get; init; }
    public required float[] TyreTempRl { get; init; }
    public required float[] TyreTempRr { get; init; }
    public required float[] GLat { get; init; }
    public required float[] GLong { get; init; }
    public required float[] WorldX { get; init; }
    public required float[] WorldY { get; init; }
    public required float[] WorldZ { get; init; }
}
