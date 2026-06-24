using Microsoft.Extensions.Logging;
using SimCoach.Pipeline;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;

namespace SimCoach.Reference;

/// <summary>
/// Owns PB selection for the <c>[references]</c> store (the repository is a plain upsert). On a clean
/// lap that beats the stored reference for its <c>(track, car, weather)</c> triple, writes the
/// resampled lap to <c>references/&lt;triple&gt;.parquet</c> and upserts the row. The replacement guard
/// — <b>faster ∧ clean ∧ not pinned</b> — lives here; a pinned reference is never auto-replaced.
/// </summary>
public sealed class ReferenceStore
{
    private readonly ReferenceRepository _repository;
    private readonly ReferenceStorageOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReferenceStore> _logger;

    public ReferenceStore(
        ReferenceRepository repository,
        ReferenceStorageOptions options,
        TimeProvider timeProvider,
        ILogger<ReferenceStore> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _repository = repository;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Replaces the reference for <paramref name="triple"/> with <paramref name="resampled"/> when the
    /// clean lap <paramref name="completed"/> beats the stored time (or none exists) and the stored row
    /// is not pinned. The caller passes only clean, fully-bounded laps. Returns <c>true</c> if the
    /// reference was updated (the caller should refresh its in-memory reference to the new PB).
    /// </summary>
    public bool MaybeUpdate(
        ReferenceTriple triple, CompletedLap completed, ResampledLap resampled, SessionIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(completed);
        ArgumentNullException.ThrowIfNull(resampled);
        ArgumentNullException.ThrowIfNull(identity);

        if (!completed.IsClean)
        {
            return false;
        }

        // Re-read the row's pinned/time state immediately before deciding — a pin set mid-session wins.
        ReferenceRow? existing =
            _repository.GetByTriple(triple.TrackId, triple.CarId, triple.WeatherBucket);
        if (existing is not null && (existing.Pinned || existing.LapTimeMs <= completed.LapTimeMs))
        {
            return false;
        }

        // source_session_id has an FK to sessions(id). SessionManager (registered before ComputeService)
        // inserts the row on frame #1, so by the time any lap completes the row exists — the FK holds.
        string parquetPath = Path.Combine(_options.Directory, triple.ParquetFileName);
        ReferenceParquetCodec.Write(resampled, parquetPath);

        _repository.Upsert(new ReferenceRow
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            TrackId = triple.TrackId,
            CarId = triple.CarId,
            WeatherBucket = triple.WeatherBucket,
            SourceSessionId = identity.SessionId,
            SourceLapNumber = completed.LapNumber,
            LapTimeMs = completed.LapTimeMs,
            ParquetPath = parquetPath,
            Pinned = false,
            CreatedAtUtc = _timeProvider.GetUtcNow(),
        });

        _logger.LogInformation(
            "Reference updated for {Track}/{Car}/{Weather}: {LapMs} ms (lap {Lap}, session {Session})",
            triple.TrackId, triple.CarId, triple.WeatherBucket, completed.LapTimeMs,
            completed.LapNumber, identity.SessionId);
        return true;
    }
}
