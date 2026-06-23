namespace SimCoach.Reference;

/// <summary>How a <see cref="TrackModel"/>'s corners were resolved (logged per session, ADR-0010).</summary>
public enum TrackModelSource
{
    /// <summary>No corners resolved — neither the landmark dataset nor a derived model is available.</summary>
    None = 0,

    /// <summary>Corners came from the vendored corner-landmark dataset (named, geometry decoupled from skill).</summary>
    Dataset = 1,

    /// <summary>Corners derived from the driver's fastest clean lap (nameless fallback for uncovered tracks).</summary>
    Derived = 2,
}

/// <summary>
/// One corner in a track model, expressed in normalized lap position (0..1). The window runs
/// <see cref="StartPosition"/> → <see cref="ApexPosition"/> → <see cref="EndPosition"/>.
/// <see cref="Name"/> is the human label from the landmark dataset, or <c>null</c> for derived
/// corners (names are a Phase-3 prompt asset, never used by compute — ADR-0010).
/// </summary>
public sealed record Corner
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public required float StartPosition { get; init; }
    public required float ApexPosition { get; init; }
    public required float EndPosition { get; init; }
}

/// <summary>
/// The corner geometry for a track. Sectors are intentionally absent — they always come from the
/// sim's own <c>current_sector_index</c> at compute time, independent of this model (ADR-0010).
/// </summary>
public sealed record TrackModel
{
    public required string TrackId { get; init; }
    public required IReadOnlyList<Corner> Corners { get; init; }
    public required TrackModelSource Source { get; init; }

    /// <summary>
    /// Lap time (ms) of the clean lap a <see cref="TrackModelSource.Derived"/> model was built from;
    /// the idempotency key for rebuilds (rebuild only on a faster lap). <c>null</c> for dataset models.
    /// </summary>
    public int? DerivedFromLapTimeMs { get; init; }
}
