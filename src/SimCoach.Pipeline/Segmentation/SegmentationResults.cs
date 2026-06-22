using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Segmentation;

/// <summary>
/// A fully-bounded lap (start-line to start-line) with its sector splits and the buffered frames the
/// C4 kernels consume. Sector times sum to <see cref="LapTimeMs"/>. <see cref="SectorTimesMs"/> has one
/// entry per sector the sim reported (3 on most tracks).
/// </summary>
public sealed record CompletedLap
{
    public required int LapNumber { get; init; }
    public required int LapTimeMs { get; init; }
    public required IReadOnlyList<int> SectorTimesMs { get; init; }
    public required bool IsClean { get; init; }
    public required IReadOnlyList<TelemetryFrame> Frames { get; init; }
}

/// <summary>One completed sector crossing: the sector that just ended and how long it took.</summary>
public sealed record SectorSplit
{
    public required int LapNumber { get; init; }
    public required int SectorIndex { get; init; }
    public required int SectorTimeMs { get; init; }
}
