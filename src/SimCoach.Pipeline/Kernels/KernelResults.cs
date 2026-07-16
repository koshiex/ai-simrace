namespace SimCoach.Pipeline.Kernels;

/// <summary>Braking signature of a corner window. Positions are <c>normalized_car_position</c> (0..1).</summary>
public sealed record BrakeProfile
{
    public required float PeakBrakePct { get; init; }

    /// <summary>Where braking began, or <c>null</c> if the driver never braked in the window.</summary>
    public float? BrakeOnPosition { get; init; }

    /// <summary>Where braking released, or <c>null</c> if braking never crossed back below the off threshold.</summary>
    public float? BrakeOffPosition { get; init; }

    /// <summary>Fraction of braking frames that also carry steering input (trail-braking).</summary>
    public required float TrailBrakePct { get; init; }
}

/// <summary>Speed/throttle signature of a corner window.</summary>
public sealed record CornerMetrics
{
    public required float MinSpeedMps { get; init; }
    public required float MinSpeedPosition { get; init; }

    /// <summary>
    /// True only when the minimum sits strictly inside the window (not on either endpoint) and dips
    /// below both endpoints — a genuine deceleration apex, not a flat/monotonic transit. When false the
    /// window has no coachable minimum-speed point, so the min-speed contribution is suppressed
    /// (D-minspeed).
    /// </summary>
    public required bool HasInSpanMinimum { get; init; }

    /// <summary>Where sustained throttle resumed after the minimum-speed point, or <c>null</c>.</summary>
    public float? ThrottleOnPosition { get; init; }
}

/// <summary>
/// Understeer / oversteer scores — a documented heuristic proxy (the inputs are native channels;
/// the score is not). Scored only over <em>steady-state mid-corner</em> frames (braking and hard
/// longitudinal-accel frames are gated out) as the mean of a scale-free front/rear slip-asymmetry
/// ratio, so each score is normalised to <c>[0,1]</c>. Higher understeer = fronts sliding more than
/// rears under steering; higher oversteer = the reverse. Both are 0 when there is no steady-state
/// cornering or no per-wheel slip data.
/// </summary>
public sealed record BalanceScores
{
    public required float UndersteerScore { get; init; }
    public required float OversteerScore { get; init; }
}

/// <summary>
/// Per-phase (entry/apex/exit) understeer/oversteer balance over one corner window, each band scored
/// INDEPENDENTLY by <see cref="BalanceKernels.AnalyzePhases"/> over the shared <see cref="CornerPhaseBands"/>
/// apex geometry — so an entry-oversteer / exit-understeer car is distinguishable from a single window
/// scalar. A band with no steady-state cornering frames carries the neutral <c>{0,0}</c>
/// <see cref="BalanceScores"/>, the same result as a balanced band.
/// </summary>
public sealed record PhaseBalanceScores
{
    public required BalanceScores Entry { get; init; }
    public required BalanceScores Apex { get; init; }
    public required BalanceScores Exit { get; init; }
}

/// <summary>
/// Tyre/brake-temp abuse summary over a lap. Peaks are the maximum across the [FL, FR, RL, RR] arrays;
/// the overheat flags compare those peaks against the kernel's abuse bands. All zero / false when the
/// sim provides no temperature channels (ACC live arrays are often empty).
/// </summary>
public sealed record ThermalResult
{
    public required float MaxTyreTempC { get; init; }
    public required float MaxBrakeTempC { get; init; }
    public required bool TyreOverheat { get; init; }
    public required bool BrakeOverheat { get; init; }
}
