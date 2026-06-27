namespace SimCoach.Reference;

/// <summary>
/// The vendored <c>cornerGeometry.json</c> document for one track: schema-versioned, length-pinned,
/// first-party baked corner geometry (ADR-0014). Written by the bake tool, read by the runtime loader;
/// carries provenance so a reviewer can see how many laps and which recording it came from.
/// </summary>
public sealed record CornerGeometryDocument
{
    /// <summary>Current on-disk schema version the loader pins against.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of this document.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>Normalized track id.</summary>
    public required string TrackId { get; init; }

    /// <summary>Lap length the normalized positions were baked against (loader checks it matches the catalog).</summary>
    public required float LapLengthM { get; init; }

    /// <summary>Number of laps the bake aggregated (provenance; fewer than 3 is not trustworthy).</summary>
    public required int LapCount { get; init; }

    /// <summary>Recording id the bake came from, if recorded (provenance only).</summary>
    public string? SourceRecording { get; init; }

    /// <summary>The baked corners in ascending position order.</summary>
    public required IReadOnlyList<CornerGeometryEntry> Corners { get; init; }

    /// <summary>
    /// Builds a document from detector output, assigning stable positional ids <c>&lt;trackId&gt;_tNN</c>.
    /// </summary>
    public static CornerGeometryDocument FromDetected(
        string trackId,
        float lapLengthM,
        int lapCount,
        IReadOnlyList<DetectedCorner> corners,
        string? sourceRecording = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentNullException.ThrowIfNull(corners);

        List<CornerGeometryEntry> entries = new(corners.Count);
        for (int i = 0; i < corners.Count; i++)
        {
            DetectedCorner corner = corners[i];
            entries.Add(new CornerGeometryEntry
            {
                Id = $"{trackId}_t{i + 1:00}",
                StartPosition = corner.StartPosition,
                ApexPosition = corner.ApexPosition,
                EndPosition = corner.EndPosition,
                ApexRadiusM = corner.ApexRadiusM,
                PeakLateralG = corner.PeakLateralG,
                Trigger = corner.Trigger.ToString(),
            });
        }

        return new CornerGeometryDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            TrackId = trackId,
            LapLengthM = lapLengthM,
            LapCount = lapCount,
            SourceRecording = sourceRecording,
            Corners = entries,
        };
    }
}
