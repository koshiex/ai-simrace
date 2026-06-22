namespace SimCoach.TestKit;

/// <summary>
/// A corner expressed in normalized lap position (0..1). The braking phase runs from
/// <see cref="EntryPos"/> to <see cref="ApexPos"/>; throttle resumes from <see cref="ApexPos"/> to
/// <see cref="ExitPos"/>. Positions must satisfy <c>0 ≤ EntryPos &lt; ApexPos &lt; ExitPos ≤ 1</c>.
/// </summary>
public sealed record SyntheticCorner
{
    /// <summary>Where braking begins (normalized lap position).</summary>
    public required float EntryPos { get; init; }

    /// <summary>Minimum-speed point (normalized lap position).</summary>
    public required float ApexPos { get; init; }

    /// <summary>Where the corner ends and the car is back at speed (normalized lap position).</summary>
    public required float ExitPos { get; init; }

    /// <summary>Speed at the apex, m/s.</summary>
    public required float MinSpeedMps { get; init; }

    /// <summary>Peak brake application in the braking phase, 0..1.</summary>
    public required float BrakePeak { get; init; }
}
