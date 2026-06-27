namespace SimCoach.Reference;

/// <summary>
/// Offline GO/NO-GO on whether a track's recorded laps are coherent enough for median-per-bin
/// centerline aggregation (ADR-0014 precondition; the offline half of the T7 check). The numbers are
/// deviations of each lap's world position from the per-bin median, in metres.
/// </summary>
public sealed record CoherenceReport
{
    /// <summary>Normalized track id the report was computed for.</summary>
    public required string TrackId { get; init; }

    /// <summary>Number of full laps that contributed samples.</summary>
    public required int LapCount { get; init; }

    /// <summary>Number of bins that had at least two laps to compare.</summary>
    public required int BinsEvaluated { get; init; }

    /// <summary>
    /// Cross-bin median of each bin's median lap-deviation — the metric that governs centerline
    /// quality. Robust: a single off-line/teleport lap does not move it.
    /// </summary>
    public required float MedianDeviationM { get; init; }

    /// <summary>95th percentile of the per-bin median deviations.</summary>
    public required float P95DeviationM { get; init; }

    /// <summary>Worst single-lap deviation in any bin (max-from-median) — exposes outliers the median rejects.</summary>
    public required float MaxDeviationM { get; init; }

    /// <summary>True when aggregation is trustworthy (enough laps and sub-threshold median deviation).</summary>
    public required bool Go { get; init; }

    /// <summary>Human-readable reasons the gate is NO-GO; empty when <see cref="Go"/> is true.</summary>
    public required IReadOnlyList<string> Reasons { get; init; }
}
