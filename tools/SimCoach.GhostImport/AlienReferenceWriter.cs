using SimCoach.Reference;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;

namespace SimCoach.GhostImport;

/// <summary>
/// Persists a decoded, seam-masked alien LINE as a <c>[references]</c> row plus its LINE-only parquet,
/// following the runtime persistence pattern (<c>ReferenceStore</c>), NOT Bake's JSON-file output. The
/// parquet is written via the shared <see cref="ReferenceParquetCodec"/> under
/// <c>&lt;DataRoot&gt;/references</c> with the kind-suffixed filename so it cannot collide with a <c>pb</c>
/// parquet on the same triple; the row is upserted with <c>kind=alien_line</c>, a non-null
/// <c>parquet_path</c>, the ghost laptime, and the ghost provenance JSON in <c>sector_sources_json</c>.
/// <c>optimal_sector_ms</c>/<c>source_session_id</c>/<c>source_lap_number</c> stay null (imported, not a
/// live PB — no ADR-0017 snapshot/prune). The row is stamped under the OWNER's triple (OD2) so it resolves
/// at <c>InitSession</c> even when the source lap was driven in a different car.
/// </summary>
internal static class AlienReferenceWriter
{
    /// <summary>
    /// Writes the parquet and upserts the <c>alien_line</c> row. Returns the parquet path written. The
    /// caller owns <paramref name="createdAtUtc"/> (the import wall-clock) so the write is deterministic.
    /// </summary>
    internal static string Persist(
        ReferenceRepository repository,
        string referencesDirectory,
        ReferenceTriple triple,
        ResampledLap alienLine,
        int lapTimeMs,
        GhostProvenance provenance,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(referencesDirectory);
        ArgumentNullException.ThrowIfNull(alienLine);
        ArgumentNullException.ThrowIfNull(provenance);

        string parquetPath =
            Path.Combine(referencesDirectory, triple.ParquetFileNameFor(ReferenceKind.AlienLine));
        ReferenceParquetCodec.Write(alienLine, parquetPath);

        repository.Upsert(new ReferenceRow
        {
            Id = Guid.NewGuid().ToString("N"),
            TrackId = triple.TrackId,
            CarId = triple.CarId,
            WeatherBucket = triple.WeatherBucket,
            SourceSessionId = null,
            SourceLapNumber = null,
            LapTimeMs = lapTimeMs,
            ParquetPath = parquetPath,
            Pinned = false,
            CreatedAtUtc = createdAtUtc,
            Kind = ReferenceKind.AlienLine.ToDbString(),
            OptimalSectorMs = null,
            SectorSourcesJson = provenance.ToJson(),
        });

        return parquetPath;
    }
}
