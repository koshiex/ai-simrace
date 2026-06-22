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

    /// <summary>Where sustained throttle resumed after the minimum-speed point, or <c>null</c>.</summary>
    public float? ThrottleOnPosition { get; init; }
}

/// <summary>
/// Understeer / oversteer scores — a documented heuristic proxy (the inputs are native channels;
/// the score is not). Higher understeer = fronts sliding more than rears under steering; higher
/// oversteer = the reverse. Both are 0 when there is no cornering or no per-wheel slip data.
/// </summary>
public sealed record BalanceScores
{
    public required float UndersteerScore { get; init; }
    public required float OversteerScore { get; init; }
}
