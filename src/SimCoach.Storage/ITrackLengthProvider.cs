namespace SimCoach.Storage;

/// <summary>
/// Supplies a track's lap length in metres. Two consumers need it: <c>SimCoach.Reference</c> converts
/// dataset landmark distances (metres) to normalized lap position, and Storage's session-end
/// <c>laps.parquet</c> conversion needs it to drive the 1 m resampler. Living in Storage (the lowest
/// project both consumers reference) keeps the seam sim-agnostic: the ACC adapter (which owns
/// <c>AccTrackCatalog</c>) implements it at the composition edge, and other sims plug in their own
/// catalog without any project depending on a sim adapter.
/// </summary>
public interface ITrackLengthProvider
{
    /// <summary>Lap length in metres for a normalized track id; <c>false</c> for unknown tracks.</summary>
    bool TryGetLapLengthM(string trackId, out float lengthM);
}
