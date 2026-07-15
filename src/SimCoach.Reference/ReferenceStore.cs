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
    private readonly ReferenceSnapshotRepository _snapshots;
    private readonly ReferenceStorageOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReferenceStore> _logger;

    public ReferenceStore(
        ReferenceRepository repository,
        ReferenceSnapshotRepository snapshots,
        ReferenceStorageOptions options,
        TimeProvider timeProvider,
        ILogger<ReferenceStore> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(snapshots);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        options.EnsureValid();
        _repository = repository;
        _snapshots = snapshots;
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
            _repository.GetByTriple(
                triple.TrackId, triple.CarId, triple.WeatherBucket, ReferenceKind.Pb.ToDbString());
        if (existing is not null && (existing.Pinned || existing.LapTimeMs <= completed.LapTimeMs))
        {
            return false;
        }

        // ADR-0017: write a VERSIONED snapshot (never overwrite), record it in the append-only history,
        // then point the active [references] row at the new file. source_session_id has an FK to
        // sessions(id); SessionManager (registered before ComputeService) inserts the row on frame #1, so
        // by the time any lap completes the row exists — the FK holds (ON DELETE SET NULL keeps history).
        string snapshotId = Guid.NewGuid().ToString("N");
        DateTimeOffset createdAtUtc = _timeProvider.GetUtcNow();
        string snapshotPath =
            Path.Combine(_options.Directory, triple.SnapshotFileName(completed.LapTimeMs, snapshotId));
        ReferenceParquetCodec.Write(resampled, snapshotPath);

        _snapshots.Insert(new ReferenceSnapshotRow
        {
            Id = snapshotId,
            TrackId = triple.TrackId,
            CarId = triple.CarId,
            WeatherBucket = triple.WeatherBucket,
            SourceSessionId = identity.SessionId,
            SourceLapNumber = completed.LapNumber,
            LapTimeMs = completed.LapTimeMs,
            ParquetPath = snapshotPath,
            CreatedAtUtc = createdAtUtc,
        });

        _repository.Upsert(new ReferenceRow
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            TrackId = triple.TrackId,
            CarId = triple.CarId,
            WeatherBucket = triple.WeatherBucket,
            SourceSessionId = identity.SessionId,
            SourceLapNumber = completed.LapNumber,
            LapTimeMs = completed.LapTimeMs,
            ParquetPath = snapshotPath,
            Pinned = false,
            CreatedAtUtc = createdAtUtc,
            Kind = ReferenceKind.Pb.ToDbString(),
        });

        PruneOldSnapshots(triple, snapshotId);

        _logger.LogInformation(
            "Reference updated for {Track}/{Car}/{Weather}: {LapMs} ms (lap {Lap}, session {Session})",
            triple.TrackId, triple.CarId, triple.WeatherBucket, completed.LapTimeMs,
            completed.LapNumber, identity.SessionId);
        return true;
    }

    /// <summary>
    /// Enforces <see cref="ReferenceStorageOptions.MaxSnapshotsPerTriple"/> by pruning the oldest
    /// snapshots (row + file) beyond the cap. The just-written active snapshot (which the
    /// <c>[references]</c> pointer was repointed at) is EXCLUDED from the prune candidates, so a coarse or
    /// tied <c>created_at_utc</c> — where oldest-first ordering falls back to a random id — can never delete
    /// the live reference file. Keeps <paramref name="cap"/> total: the active plus the newest cap-1 others.
    /// </summary>
    private void PruneOldSnapshots(ReferenceTriple triple, string activeSnapshotId)
    {
        if (_options.MaxSnapshotsPerTriple is not int cap)
        {
            return; // keep-all (default)
        }

        List<ReferenceSnapshotRow> prunable =
        [
            .. _snapshots.ListByTriple(triple.TrackId, triple.CarId, triple.WeatherBucket)
                .Where(s => !string.Equals(s.Id, activeSnapshotId, StringComparison.Ordinal)),
        ];
        int excess = prunable.Count - (cap - 1); // reserve one slot for the always-kept active snapshot
        for (int i = 0; i < excess; i++)
        {
            ReferenceSnapshotRow old = prunable[i];
            _snapshots.Delete(old.Id);
            TryDeleteFile(old.ParquetPath);
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            // Non-fatal: the history row is already gone, so an undeleted file is a harmless orphan that
            // no pointer resolves to. Log rather than fail the reference update.
            _logger.LogWarning(ex, "Failed to prune reference snapshot file {Path}", path);
        }
    }
}
