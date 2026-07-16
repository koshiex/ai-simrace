using Microsoft.Extensions.Logging;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;

namespace SimCoach.Reference;

/// <summary>
/// Reads the stored reference (PB) lap for a <c>(track, car, weather)</c> triple as a resampled grid,
/// or <c>null</c> until a PB exists (the first session on a triple has no reference, so the coach stays
/// quiet on deltas). Resolves the row via <see cref="ReferenceRepository"/> and decodes its parquet
/// with <see cref="ReferenceParquetCodec"/>.
/// </summary>
public sealed class ReferenceLookup
{
    private readonly ReferenceRepository _repository;
    private readonly ILogger<ReferenceLookup> _logger;

    public ReferenceLookup(ReferenceRepository repository, ILogger<ReferenceLookup> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// The resampled reference lap for the triple and <paramref name="kind"/> (default <c>Pb</c>), or
    /// <c>null</c> when no such reference is stored. PR-B3 (M4): the single lookup is kind-parameterized —
    /// the <c>alien_line</c> LINE grid is read through the same path as the <c>pb</c> TIME grid, no second
    /// same-type singleton. A file-full kind (<c>pb</c>/<c>alien_line</c>) with a null <c>parquet_path</c>
    /// is a corrupt row and hard-throws; a missing file degrades to <c>null</c>.
    /// </summary>
    public ResampledLap? Get(ReferenceTriple triple, ReferenceKind kind = ReferenceKind.Pb)
    {
        string kindDb = kind.ToDbString();
        ReferenceRow? row = _repository.GetByTriple(
            triple.TrackId, triple.CarId, triple.WeatherBucket, kindDb);
        if (row is null)
        {
            return null;
        }

        // Only the row-only `optimal` kind is legitimately file-less; a pb/alien_line row with no
        // parquet_path is a corrupt reference, not a quiet "no reference yet" — surface it (ADR-0021).
        if (row.ParquetPath is null)
        {
            _logger.LogError(
                "{Kind} reference row {Id} for {Track}/{Car}/{Weather} has a null parquet_path",
                kindDb, row.Id, triple.TrackId, triple.CarId, triple.WeatherBucket);
            throw new InvalidOperationException(
                $"{kindDb} reference row '{row.Id}' has no parquet_path.");
        }

        if (!File.Exists(row.ParquetPath))
        {
            return null;
        }

        return ReferenceParquetCodec.Read(row.ParquetPath);
    }

    /// <summary>
    /// M7 diagnostic: the weather bucket of an <c>alien_line</c> row stored for <paramref name="triple"/>'s
    /// <c>(track, car)</c> under a DIFFERENT bucket than the live one, or <c>null</c> when none exists.
    /// <see cref="GetByTriple"/> keys weather exactly (OD6), so a bucket-mismatched import silently never
    /// resolves; this lets the caller surface that as a debuggable info line. Read-only — changes nothing.
    /// </summary>
    public string? FindAlienLineWeatherMismatch(ReferenceTriple triple)
    {
        foreach (ReferenceRow row in _repository.GetAllByKind(ReferenceKind.AlienLine.ToDbString()))
        {
            if (string.Equals(row.TrackId, triple.TrackId, StringComparison.Ordinal)
                && string.Equals(row.CarId, triple.CarId, StringComparison.Ordinal)
                && !string.Equals(row.WeatherBucket, triple.WeatherBucket, StringComparison.Ordinal))
            {
                return row.WeatherBucket;
            }
        }

        return null;
    }
}
