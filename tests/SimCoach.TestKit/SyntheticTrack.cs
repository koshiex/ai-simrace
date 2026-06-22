namespace SimCoach.TestKit;

/// <summary>
/// A track described purely by geometry needed to synthesize telemetry: lap length, sector count and
/// a corner layout. Lap length is supplied directly (no dependency on <c>AccTrackCatalog</c>), so the
/// fixture stays decoupled from the ACC adapter.
/// </summary>
public sealed record SyntheticTrack
{
    /// <summary>Normalized track id, e.g. <c>"spa"</c>.</summary>
    public required string TrackId { get; init; }

    /// <summary>Lap length in metres.</summary>
    public required float LapLengthM { get; init; }

    /// <summary>Number of sectors the sim reports (sector splits are equal fractions of the lap).</summary>
    public required int SectorCount { get; init; }

    /// <summary>Corners ordered by entry position.</summary>
    public required IReadOnlyList<SyntheticCorner> Corners { get; init; }
}
