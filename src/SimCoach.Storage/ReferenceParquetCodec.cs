using ParquetSharp;

namespace SimCoach.Storage;

/// <summary>
/// Reads and writes a single reference lap (the resampled PB) to its own Parquet file
/// (<c>references/&lt;track&gt;_&lt;car&gt;_&lt;weather&gt;.parquet</c>). A reference is one lap = one
/// row group, sharing the exact column schema of <c>laps.parquet</c> via
/// <see cref="ResampledLapParquet"/> so the same reader serves both. <c>lap_number</c> is informational
/// for a reference (all rows carry the source lap number).
/// </summary>
public static class ReferenceParquetCodec
{
    /// <summary>Writes <paramref name="lap"/> to <paramref name="outputPath"/> as a single row group.</summary>
    public static void Write(ResampledLap lap, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var writer = new ParquetFileWriter(outputPath, ResampledLapParquet.BuildColumns());
        ResampledLapParquet.WriteRowGroup(writer, lap);
        // The `using` disposes the writer, which flushes the footer.
    }

    /// <summary>Reads the single reference lap back from <paramref name="path"/>.</summary>
    public static ResampledLap Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Reference parquet '{path}' does not exist.", path);
        }

        try
        {
            using var reader = new ParquetFileReader(path);
            // A reference is exactly one lap = one row group. Reject anything else (e.g. a path
            // accidentally pointed at a multi-lap laps.parquet) rather than silently reading only lap 1.
            if (reader.FileMetaData.NumRowGroups != 1)
            {
                throw new InvalidDataException(
                    $"Reference parquet '{path}' must have exactly one row group, found {reader.FileMetaData.NumRowGroups}.");
            }

            using RowGroupReader rowGroup = reader.RowGroup(0);
            return ResampledLapParquet.ReadRowGroup(rowGroup);
        }
        catch (ParquetException ex)
        {
            // A truncated / non-parquet / garbage file throws ParquetException straight from the reader ctor
            // (before the row-group check) or a column read. Present it as the domain "corrupt parquet" signal
            // so fault-isolating callers (e.g. ComputeSession's M3 alien-line guard) catch it uniformly with
            // the row-group case instead of it escaping as an unfiltered exception and poisoning the session.
            throw new InvalidDataException($"Reference parquet '{path}' could not be read: {ex.Message}", ex);
        }
    }
}
