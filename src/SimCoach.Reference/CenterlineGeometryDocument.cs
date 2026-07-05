namespace SimCoach.Reference;

/// <summary>
/// The vendored <c>centerline.&lt;trackId&gt;.json</c> document for one track: schema-versioned,
/// length-pinned, first-party baked median corridor centerline (ADR-0014 / ADR-0019). Written by the bake
/// tool alongside <c>cornerGeometry.json</c>, read by the runtime <see cref="CenterlineGeometryDataset"/> to
/// serve as the LINE reference (M38) — distinct from the PB TIME reference. Wraps the same
/// <see cref="MedianCenterline"/> the bake already builds; no second derivation.
/// </summary>
public sealed record CenterlineGeometryDocument
{
    /// <summary>Current on-disk schema version the loader pins against.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of this document.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>Normalized track id.</summary>
    public required string TrackId { get; init; }

    /// <summary>Lap length the bins were baked against (loader checks it matches the catalog).</summary>
    public required float LapLengthM { get; init; }

    /// <summary>Number of laps the bake aggregated (provenance; below <see cref="MedianCenterlineBuilder.MinLapsForTrust"/> is not trustworthy).</summary>
    public required int LapCount { get; init; }

    /// <summary>The median centerline bins in ascending distance order.</summary>
    public required IReadOnlyList<CenterlineBin> Bins { get; init; }

    /// <summary>Wraps an in-memory <see cref="MedianCenterline"/> into a serializable document.</summary>
    public static CenterlineGeometryDocument FromCenterline(MedianCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(centerline);
        return new CenterlineGeometryDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            TrackId = centerline.TrackId,
            LapLengthM = centerline.LapLengthM,
            LapCount = centerline.LapCount,
            Bins = centerline.Bins,
        };
    }

    /// <summary>Reconstructs the in-memory <see cref="MedianCenterline"/> from the document.</summary>
    public MedianCenterline ToCenterline() => new()
    {
        TrackId = TrackId,
        LapLengthM = LapLengthM,
        LapCount = LapCount,
        Bins = Bins,
    };
}
