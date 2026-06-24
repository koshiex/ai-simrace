using Microsoft.Extensions.Logging;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Storage;

namespace SimCoach.Reference;

/// <summary>
/// Resolves a track's corner model in priority order (ADR-0010): vendored landmark dataset →
/// persisted derived model → none. The dataset path resolves at session start; the derive path only
/// after the first clean lap completes (runtime dependency on C3/C4), so a fresh uncovered track
/// starts at <see cref="TrackModelSource.None"/> and gains corners mid-session via <see cref="Derive"/>.
/// Sectors are never part of this model — they always come from the sim.
/// </summary>
public sealed class TrackModelStore
{
    private readonly LandmarkDataset _dataset;
    private readonly ITrackModelRepository _repository;
    private readonly ITrackLengthProvider _trackLengths;
    private readonly ILogger<TrackModelStore> _logger;

    public TrackModelStore(
        LandmarkDataset dataset,
        ITrackModelRepository repository,
        ITrackLengthProvider trackLengths,
        ILogger<TrackModelStore> logger)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(trackLengths);
        ArgumentNullException.ThrowIfNull(logger);
        _dataset = dataset;
        _repository = repository;
        _trackLengths = trackLengths;
        _logger = logger;
    }

    /// <summary>
    /// The current best model for a track. Logs the resolved source (<c>dataset | derived | none</c>).
    /// </summary>
    public TrackModel Get(string trackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);

        if (_trackLengths.TryGetLapLengthM(trackId, out float lapLengthM)
            && _dataset.TryGetCorners(trackId, lapLengthM, out IReadOnlyList<Corner> datasetCorners))
        {
            _logger.LogInformation("Track model for {TrackId}: dataset ({Count} corners)", trackId, datasetCorners.Count);
            return new TrackModel { TrackId = trackId, Corners = datasetCorners, Source = TrackModelSource.Dataset };
        }

        TrackModel? persisted = _repository.Get(trackId);
        if (persisted is { Source: TrackModelSource.Derived })
        {
            _logger.LogInformation(
                "Track model for {TrackId}: derived ({Count} corners)", trackId, persisted.Corners.Count);
            return persisted;
        }

        _logger.LogInformation("Track model for {TrackId}: none (corner events suppressed)", trackId);
        return new TrackModel { TrackId = trackId, Corners = [], Source = TrackModelSource.None };
    }

    /// <summary>
    /// Derives a model from a clean lap and persists it, but only if it improves on the stored derived
    /// model (faster lap) — idempotent on a slower/equal lap. The caller passes only clean, fully-bounded
    /// laps. Returns the model now in effect for the track.
    /// </summary>
    public TrackModel Derive(string trackId, CompletedLap cleanLap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentNullException.ThrowIfNull(cleanLap);

        TrackModel? existing = _repository.Get(trackId);
        if (existing is { Source: TrackModelSource.Derived, DerivedFromLapTimeMs: int storedMs }
            && storedMs <= cleanLap.LapTimeMs)
        {
            _logger.LogInformation(
                "Track model for {TrackId}: kept derived model from {StoredMs} ms (candidate {CandidateMs} ms not faster)",
                trackId, storedMs, cleanLap.LapTimeMs);
            return existing;
        }

        TrackModel derived = TrackModelBuilder.Build(trackId, cleanLap);
        _repository.Save(derived);
        _logger.LogInformation(
            "Track model for {TrackId}: derived {Count} corners from a {LapMs} ms clean lap",
            trackId, derived.Corners.Count, cleanLap.LapTimeMs);
        return derived;
    }
}
