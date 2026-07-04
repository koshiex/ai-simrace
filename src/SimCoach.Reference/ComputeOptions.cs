namespace SimCoach.Reference;

/// <summary>
/// Tuning for <see cref="ComputeService"/>. Kernel thresholds (brake hysteresis, trail-brake) stay as
/// named constants inside the C4 kernels; these are the compute-orchestration knobs.
/// </summary>
public sealed class ComputeOptions
{
    /// <summary>
    /// Sustained throttle fraction that marks a driver as back on power. No longer gates corner
    /// emission (M2 fires on the geometric corner end); retained for its validated range and any
    /// future exit-metric reconciliation with the Pipeline kernel's own constant.
    /// </summary>
    public float ResumeThrottlePct { get; init; } = 0.5f;

    /// <summary>
    /// How far upstream of a corner's <see cref="Corner.StartPosition"/> the corner window arms, in
    /// metres, so the real braking zone (ACC brake-on lands 41–290 m before the geometric start) falls
    /// inside the scanned span. Feeds the brake-onset slice only (M16); the delta/min-speed/trail-brake
    /// sub-window stays strictly <c>[Start, End]</c> (M2). Q7-resolved from the pre-gate onset spread.
    /// </summary>
    public float BrakeWindowUpstreamM { get; init; } = 300f;

    /// <summary>
    /// M3 Tier-A ceiling: the largest per-corner reference-relative delta (either sign) that is
    /// physically plausible. A larger magnitude is a detection artefact and is neutralised before
    /// phrasing so <c>corner_catch_all</c> cannot voice a fabricated <c>abs(delta_ms)</c> loss.
    /// Placeholder default — final value user-owned. TODO(Q3b).
    /// </summary>
    public int MaxPlausibleCornerLossMs { get; init; } = 2000;

    /// <summary>
    /// M3 Tier-A ceiling for a single sector crossing. A sector spans several corners, so its plausible
    /// delta is larger than a corner's; this backstops the grossly implausible out-lap crossing
    /// (~30000 ms) that would emit if the M1 latch regressed. Distinct from
    /// <see cref="MaxPlausibleCornerLossMs"/> so gating sectors at the corner ceiling does not
    /// over-suppress legitimate multi-second sector losses. Placeholder default — final value
    /// user-owned. TODO(Q3b).
    /// </summary>
    public int MaxPlausibleSectorLossMs { get; init; } = 10000;

    /// <summary>
    /// M3 Tier-B budget multiplier: a loss is plausible only up to <c>ratio × |lap deficit|</c> (floored
    /// by <see cref="LapDeficitFloorMs"/>). Compared against the lap DEFICIT, never a sector absolute.
    /// Placeholder default — final value user-owned. TODO(Q3b).
    /// </summary>
    public float LapDeficitLossRatio { get; init; } = 1.0f;

    /// <summary>
    /// M3 Tier-B floor: the minimum deficit budget, so a near-zero lap deficit still admits genuinely
    /// small losses and never collapses the budget to nothing. Placeholder default — final value
    /// user-owned. TODO(Q3b).
    /// </summary>
    public int LapDeficitFloorMs { get; init; } = 300;

    /// <summary>How many corners to report in a lap/sector <c>top_losses</c> list.</summary>
    public int TopLossesCount { get; init; } = 3;

    /// <summary>Top-N bound on the session-level <c>aggregated_losses</c> (mirrors the debrief schema cap).</summary>
    public int AggregatedLossesCap { get; init; } = 5;

    public void EnsureValid()
    {
        if (ResumeThrottlePct is <= 0f or > 1f)
        {
            throw new InvalidOperationException("ComputeOptions.ResumeThrottlePct must be in (0, 1].");
        }

        if (BrakeWindowUpstreamM < 0f)
        {
            throw new InvalidOperationException("ComputeOptions.BrakeWindowUpstreamM must be non-negative.");
        }

        if (MaxPlausibleCornerLossMs <= 0)
        {
            throw new InvalidOperationException("ComputeOptions.MaxPlausibleCornerLossMs must be positive.");
        }

        if (MaxPlausibleSectorLossMs <= 0)
        {
            throw new InvalidOperationException("ComputeOptions.MaxPlausibleSectorLossMs must be positive.");
        }

        if (LapDeficitLossRatio <= 0f)
        {
            throw new InvalidOperationException("ComputeOptions.LapDeficitLossRatio must be positive.");
        }

        if (LapDeficitFloorMs < 0)
        {
            throw new InvalidOperationException("ComputeOptions.LapDeficitFloorMs must be non-negative.");
        }

        if (TopLossesCount < 0)
        {
            throw new InvalidOperationException("ComputeOptions.TopLossesCount must be non-negative.");
        }

        if (AggregatedLossesCap < 0)
        {
            throw new InvalidOperationException("ComputeOptions.AggregatedLossesCap must be non-negative.");
        }
    }
}
