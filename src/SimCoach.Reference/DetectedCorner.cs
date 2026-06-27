namespace SimCoach.Reference;

/// <summary>
/// A corner found by <see cref="CornerCenterlineDetector"/> on the aggregate centerline, in normalized
/// 0..1 position (the contract the rest of the pipeline consumes). Carries diagnostics the bake review
/// page surfaces; the apex is the argmax of centerline curvature, never the argmax of lateral g (ADR-0014).
/// </summary>
public sealed record DetectedCorner
{
    /// <summary>Corner entry, normalized 0..1.</summary>
    public required float StartPosition { get; init; }

    /// <summary>Apex (max centerline curvature), normalized 0..1.</summary>
    public required float ApexPosition { get; init; }

    /// <summary>Corner exit, normalized 0..1.</summary>
    public required float EndPosition { get; init; }

    /// <summary>Radius at the apex in metres (1/|curvature|); positive infinity for a near-straight.</summary>
    public required float ApexRadiusM { get; init; }

    /// <summary>Peak median |lateral g| inside the corner window.</summary>
    public required float PeakLateralG { get; init; }

    /// <summary>Which channel triggered detection.</summary>
    public required CornerChannel Trigger { get; init; }
}
