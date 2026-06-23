namespace SimCoach.Reference;

/// <summary>
/// Supplies a track's lap length in metres, used to convert dataset landmark distances (metres) to
/// normalized lap position. A tiny seam so <c>SimCoach.Reference</c> stays sim-agnostic: the ACC
/// adapter (which owns <c>AccTrackCatalog</c>) implements it at the composition edge, and other sims
/// plug in their own catalog without this project depending on any sim adapter.
/// </summary>
public interface ITrackLengthProvider
{
    /// <summary>Lap length in metres for a normalized track id; <c>false</c> for unknown tracks.</summary>
    bool TryGetLapLengthM(string trackId, out float lengthM);
}
