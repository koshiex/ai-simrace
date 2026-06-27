using Microsoft.Extensions.Logging;
using SimCoach.Storage;

namespace SimCoach.Reference;

/// <summary>
/// Resolves a track's corner model from first-party baked geometry (ADR-0014): baked → none. The
/// geometry resolves once at session start and is fixed for the session — there is no mid-session
/// derive. A track with no baked geometry resolves to <see cref="TrackModelSource.None"/> (corner
/// events suppressed). Sectors are never part of this model — they always come from the sim.
/// </summary>
public sealed class TrackModelStore
{
    private readonly CornerGeometryDataset _geometry;
    private readonly ITrackLengthProvider _trackLengths;
    private readonly ILogger<TrackModelStore> _logger;

    public TrackModelStore(
        CornerGeometryDataset geometry,
        ITrackLengthProvider trackLengths,
        ILogger<TrackModelStore> logger)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(trackLengths);
        ArgumentNullException.ThrowIfNull(logger);
        _geometry = geometry;
        _trackLengths = trackLengths;
        _logger = logger;
    }

    /// <summary>The model for a track. Logs the resolved source (<c>baked | none</c>).</summary>
    public TrackModel Get(string trackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);

        if (_trackLengths.TryGetLapLengthM(trackId, out float lapLengthM)
            && _geometry.TryGetCorners(trackId, lapLengthM, out IReadOnlyList<Corner> corners))
        {
            _logger.LogInformation("Track model for {TrackId}: baked ({Count} corners)", trackId, corners.Count);
            return new TrackModel { TrackId = trackId, Corners = corners, Source = TrackModelSource.Baked };
        }

        _logger.LogInformation("Track model for {TrackId}: none (corner events suppressed)", trackId);
        return new TrackModel { TrackId = trackId, Corners = [], Source = TrackModelSource.None };
    }
}
