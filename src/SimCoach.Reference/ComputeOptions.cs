namespace SimCoach.Reference;

/// <summary>
/// Tuning for <see cref="ComputeService"/>. Kernel thresholds (brake hysteresis, trail-brake) stay as
/// named constants inside the C4 kernels; these are the compute-orchestration knobs.
/// </summary>
public sealed class ComputeOptions
{
    /// <summary>Sustained throttle fraction that marks the corner-exit trigger (back on power).</summary>
    public float ResumeThrottlePct { get; init; } = 0.5f;

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
