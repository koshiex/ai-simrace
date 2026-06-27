namespace SimCoach.Reference;

/// <summary>
/// One baked corner in <c>cornerGeometry.json</c>: positions are normalized 0..1 (the contract the
/// pipeline consumes); the rest are diagnostics carried for the review page. <see cref="Trigger"/> is the
/// <see cref="CornerChannel"/> name as a string so the JSON stays stable if the enum is reordered.
/// </summary>
public sealed record CornerGeometryEntry
{
    /// <summary>Stable positional id, <c>&lt;trackId&gt;_tNN</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Corner entry, normalized 0..1.</summary>
    public required float StartPosition { get; init; }

    /// <summary>Apex (max centerline curvature), normalized 0..1.</summary>
    public required float ApexPosition { get; init; }

    /// <summary>Corner exit, normalized 0..1.</summary>
    public required float EndPosition { get; init; }

    /// <summary>Radius at the apex in metres.</summary>
    public required float ApexRadiusM { get; init; }

    /// <summary>Peak median |lateral g| inside the corner window.</summary>
    public required float PeakLateralG { get; init; }

    /// <summary>Which channel triggered detection (<see cref="CornerChannel"/> name).</summary>
    public required string Trigger { get; init; }
}
