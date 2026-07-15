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

    /// <summary>The resampled reference lap for the triple, or <c>null</c> when no PB is stored.</summary>
    public ResampledLap? Get(ReferenceTriple triple)
    {
        ReferenceRow? row = _repository.GetByTriple(
            triple.TrackId, triple.CarId, triple.WeatherBucket, ReferenceKind.Pb.ToDbString());
        if (row is null)
        {
            return null;
        }

        // Only the row-only `optimal` kind is legitimately file-less; a pb/alien_line row with no
        // parquet_path is a corrupt reference, not a quiet "no PB yet" — surface it (ADR-0021).
        if (row.ParquetPath is null)
        {
            _logger.LogError(
                "PB reference row {Id} for {Track}/{Car}/{Weather} has a null parquet_path",
                row.Id, triple.TrackId, triple.CarId, triple.WeatherBucket);
            throw new InvalidOperationException(
                $"PB reference row '{row.Id}' has no parquet_path.");
        }

        if (!File.Exists(row.ParquetPath))
        {
            return null;
        }

        return ReferenceParquetCodec.Read(row.ParquetPath);
    }
}
