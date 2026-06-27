namespace SimCoach.Reference;

/// <summary>How a <see cref="TrackModel"/>'s corners were resolved (logged per session, ADR-0014).</summary>
public enum TrackModelSource
{
    /// <summary>No corners resolved — the track has no baked geometry yet (corner events suppressed).</summary>
    None = 0,

    /// <summary>Corners came from the first-party baked geometry (cornerGeometry.json); fixed for the session.</summary>
    Baked = 1,
}

/// <summary>
/// One corner in a track model, expressed in normalized lap position (0..1). The window runs
/// <see cref="StartPosition"/> → <see cref="ApexPosition"/> → <see cref="EndPosition"/>.
/// <see cref="Name"/> is null today — names are a Phase-3 prompt asset, never used by compute
/// (ADR-0010); the baked geometry ships positional <c>corner_id</c>s only.
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
}
