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

    public ReferenceLookup(ReferenceRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <summary>The resampled reference lap for the triple, or <c>null</c> when no PB is stored.</summary>
    public ResampledLap? Get(ReferenceTriple triple)
    {
        ReferenceRow? row = _repository.GetByTriple(triple.TrackId, triple.CarId, triple.WeatherBucket);
        if (row is null || !File.Exists(row.ParquetPath))
        {
            return null;
        }

        return ReferenceParquetCodec.Read(row.ParquetPath);
    }
}
