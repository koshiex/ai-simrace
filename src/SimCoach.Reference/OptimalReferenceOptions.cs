namespace SimCoach.Reference;

/// <summary>
/// Tuning for the own-optimal ("theoretical best") reference builder (M46). Every threshold is
/// config-driven — no magic numbers in <see cref="OptimalReferenceBuilder"/>. Bound from the
/// <c>Reference:Optimal</c> configuration section and registered as a validated concrete singleton (NOT
/// <c>IOptions&lt;&gt;</c>) at the composition edge — <see cref="EnsureValid"/> runs at startup, matching
/// the repo's existing concrete-options pattern (e.g. <c>ComputeOptions</c>).
/// </summary>
public sealed record OptimalReferenceOptions
{
    /// <summary>
    /// Minimum lap-time gain (PB − Σ best sectors) required to store an optimal. Below it the PB already is
    /// the target, so no optimal is written. User-facing default (owner-decided ~150 ms).
    /// </summary>
    public int MinOptimalGainMs { get; init; } = 150;

    /// <summary>
    /// Per-sector outlier guard (must-fix #3): a sector-best candidate is rejected when it sits more than
    /// this many ms BELOW that sector's clean-time median. Catches tow / undetected-cut / grip-spike
    /// poisoning independent of lap age. The wider of this and the robust-stddev band applies.
    /// </summary>
    public int MaxSectorOutlierMs { get; init; } = 1500;

    /// <summary>
    /// The robust-stddev arm of the outlier guard: a candidate is also allowed down to
    /// <c>median − multiple × robustStddev</c> (robust stddev = MAD × 1.4826). The effective floor is the
    /// wider (more permissive) of this band and <see cref="MaxSectorOutlierMs"/>, so a genuinely fast lap
    /// in a high-variance sector is not falsely rejected.
    /// </summary>
    public double OutlierRobustStddevMultiple { get; init; } = 4.0;

    /// <summary>
    /// Cheap per-lap consistency filter: a clean lap whose <c>Σ sectors</c> differs from its recorded
    /// <c>lap_time_ms</c> by more than this is dropped as a timing glitch before its sectors enter any
    /// distribution. Small (sectors partition the same ACC lap timer, ~ms rounding at 2 seams).
    /// </summary>
    public int LapSumToleranceMs { get; init; } = 200;

    public void EnsureValid()
    {
        if (MinOptimalGainMs < 0)
        {
            throw new InvalidOperationException("OptimalReferenceOptions.MinOptimalGainMs must be non-negative.");
        }

        if (MaxSectorOutlierMs < 0)
        {
            throw new InvalidOperationException("OptimalReferenceOptions.MaxSectorOutlierMs must be non-negative.");
        }

        if (OutlierRobustStddevMultiple < 0)
        {
            throw new InvalidOperationException(
                "OptimalReferenceOptions.OutlierRobustStddevMultiple must be non-negative.");
        }

        if (LapSumToleranceMs < 0)
        {
            throw new InvalidOperationException("OptimalReferenceOptions.LapSumToleranceMs must be non-negative.");
        }
    }
}
