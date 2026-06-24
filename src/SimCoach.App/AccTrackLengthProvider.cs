using SimCoach.Adapters.ACC;
using SimCoach.Storage;

namespace SimCoach.App;

/// <summary>
/// Composition-edge bridge from the ACC-specific <see cref="AccTrackCatalog"/> (static) to the
/// sim-agnostic <see cref="ITrackLengthProvider"/> seam consumed by <c>TrackModelStore</c> and the
/// session-end <c>laps.parquet</c> conversion. App is the only project allowed to reference the ACC
/// adapter, so the wrapper lives here; another sim plugs in its own provider the same way.
/// </summary>
internal sealed class AccTrackLengthProvider : ITrackLengthProvider
{
    public bool TryGetLapLengthM(string trackId, out float lengthM) =>
        AccTrackCatalog.TryGetLapLengthM(trackId, out lengthM);
}
