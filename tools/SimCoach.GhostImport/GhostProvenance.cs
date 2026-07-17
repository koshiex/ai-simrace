using System.Text.Json;

namespace SimCoach.GhostImport;

/// <summary>
/// Audit trail for an imported alien LINE, serialized (<see cref="System.Text.Json"/>, never Newtonsoft)
/// into the <c>[references].sector_sources_json</c> column of the <c>alien_line</c> row (PR-B3 commit 21).
/// Records only the source lap's provenance — the accreplay lap id, the car it was driven in, the laptime,
/// and the track. The driver name is deliberately absent: it is dropped at parse and is never a field here
/// (OD1), so no persisted artifact can ever carry a third party's name.
/// </summary>
internal sealed record GhostProvenance
{
    private const string AccReplaySource = "accreplay";

    /// <summary>Where the ghost was fetched from (only accreplay today).</summary>
    public required string Source { get; init; }

    /// <summary>The accreplay leaderboard lap id the alien LINE was derived from.</summary>
    public required long LapId { get; init; }

    /// <summary>The car the source lap was driven in (may differ from the owner triple's car — OD2).</summary>
    public required string SourceCar { get; init; }

    /// <summary>The source lap time in milliseconds (stored on the row's <c>lap_time_ms</c> too).</summary>
    public required int LapTimeMs { get; init; }

    /// <summary>The normalized track id the lap belongs to.</summary>
    public required string TrackId { get; init; }

    /// <summary>Builds provenance from an accreplay leaderboard entry; the entry carries no driver name.</summary>
    internal static GhostProvenance FromAccReplay(AccReplayLap lap, string trackId) => new()
    {
        Source = AccReplaySource,
        LapId = lap.LapId,
        SourceCar = lap.Car,
        LapTimeMs = lap.LapTimeMs,
        TrackId = trackId,
    };

    /// <summary>Serializes to the compact JSON stored in <c>sector_sources_json</c>.</summary>
    internal string ToJson() => JsonSerializer.Serialize(this);
}
