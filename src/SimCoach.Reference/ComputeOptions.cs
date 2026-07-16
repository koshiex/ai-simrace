namespace SimCoach.Reference;

/// <summary>
/// Tuning for <see cref="ComputeService"/>. Kernel thresholds (brake hysteresis, trail-brake) stay as
/// named constants inside the C4 kernels; these are the compute-orchestration knobs.
/// </summary>
public sealed record ComputeOptions
{
    /// <summary>
    /// Tier-2 (internal/advanced — NOT a user slider): half-width fraction of the apex band, the SINGLE
    /// shared definition of "apex". It scopes the brake-overlap metric to the turn-in → apex window and
    /// MUST equal <c>RuleEngineOptions.ApexWindowFraction</c> (the live corner-phase gate's fraction).
    /// The App composition edge binds ONE value from <c>Coach:Rules:ApexWindowFraction</c> and feeds it
    /// to both, so the two can never drift; <see cref="EnsureValid"/> only range-checks it (0 &lt; x ≤ 0.5),
    /// with the cross-options equality asserted at that edge where both values are visible.
    /// </summary>
    public double ApexWindowFraction { get; init; } = 0.25;

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

    /// <summary>
    /// Tier-2 (internal): the largest apex radius (metres) for which line-shape coaching is meaningful. A
    /// baked corner whose apex radius exceeds this is a fast kink / near-straight — its signed per-phase
    /// line deviations (M34) are neutralised to 0 (M38 corner-type gate). A corner with no baked radius
    /// (0) is never gated here (the kernel's geometric neutralisation still applies).
    /// <para>
    /// PR-B3 alien-regime review (MUST-FIX #5, OD7): this ceiling and the <c>Trigger == "LateralG"</c>
    /// neutralisation are KEPT as-is for the alien LINE reference. Against a real 2–4 m pro corridor the
    /// same fast corners now show genuine offsets, but the owner decision is to LEAVE fast/LateralG corners
    /// intentionally NOT signed-line-coached (apex is still handled by the now-seam-gated unsigned cue). No
    /// per-kind <c>AlienLineDeviationFloorM</c> is added — that ships only on explicit owner request. The
    /// gate stays config-driven here so lowering this ceiling makes a fast-corner alien difference coachable
    /// (no magic number), asserted by <c>AlienRegimeGateTests</c>.
    /// </para>
    /// </summary>
    public float LineRelevanceMaxRadiusM { get; init; } = 300f;

    /// <summary>
    /// M36 cross-unit ranking scale: how many milliseconds one metre of brake-point error is worth when
    /// choosing a corner's <c>dominant_channel</c>. The three <c>MsPer*</c> scales bring the signed
    /// diagnostic diffs (metres, metres, km/h) onto one comparable ms axis so the argmax is not decided by
    /// unit magnitude. A ranking heuristic only — the product is never summed into <c>total_loss_ms</c>.
    /// Placeholder default — final value user-owned.
    /// </summary>
    public float MsPerMetreBrakePoint { get; init; } = 10f;

    /// <summary>
    /// M36 cross-unit ranking scale: milliseconds per metre of throttle-resume error, for the
    /// <c>dominant_channel</c> argmax. See <see cref="MsPerMetreBrakePoint"/>. Placeholder default.
    /// </summary>
    public float MsPerMetreThrottleResume { get; init; } = 10f;

    /// <summary>
    /// M36 cross-unit ranking scale: milliseconds per km/h of min-speed deficit, for the
    /// <c>dominant_channel</c> argmax. See <see cref="MsPerMetreBrakePoint"/>. Placeholder default.
    /// </summary>
    public float MsPerKmhMinSpeed { get; init; } = 20f;

    public void EnsureValid()
    {
        if (ApexWindowFraction is <= 0 or > 0.5)
        {
            throw new InvalidOperationException("ComputeOptions.ApexWindowFraction must be in (0, 0.5].");
        }

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

        if (LineRelevanceMaxRadiusM <= 0f)
        {
            throw new InvalidOperationException("ComputeOptions.LineRelevanceMaxRadiusM must be positive.");
        }

        if (MsPerMetreBrakePoint <= 0f)
        {
            throw new InvalidOperationException("ComputeOptions.MsPerMetreBrakePoint must be positive.");
        }

        if (MsPerMetreThrottleResume <= 0f)
        {
            throw new InvalidOperationException("ComputeOptions.MsPerMetreThrottleResume must be positive.");
        }

        if (MsPerKmhMinSpeed <= 0f)
        {
            throw new InvalidOperationException("ComputeOptions.MsPerKmhMinSpeed must be positive.");
        }
    }
}
