namespace SimCoach.Reference;

/// <summary>
/// Persists derived track models so an uncovered track is not re-derived every session. Dataset
/// models are not persisted (they are recomputed from the vendored file on demand).
/// </summary>
public interface ITrackModelRepository
{
    /// <summary>The persisted model for a track, or <c>null</c> if none has been saved.</summary>
    TrackModel? Get(string trackId);

    /// <summary>Writes (overwrites) the model for its track id.</summary>
    void Save(TrackModel model);
}
