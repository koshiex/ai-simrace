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
