using ParquetSharp;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Storage.Mcap;

namespace SimCoach.Storage;

/// <summary>
/// Converts a session's segment directory into <c>laps.parquet</c> — one row group per fully-bounded
/// lap, each lap resampled to the 1 m position grid. Runs off the hot path (end of session). Reads the
/// segments as one logical stream (<see cref="McapSegmentEnumerator"/>), splits laps with
/// <see cref="LapSegmenter"/>, resamples with <see cref="PositionResampler"/>, and writes with
/// ParquetSharp. The column order is the explicit <c>data-model.md</c> order (not protobuf field order)
/// and includes <c>world_x/y/z</c> for racing-line deviation.
/// </summary>
public static class LapParquetWriter
{
    /// <summary>
    /// Writes <paramref name="outputPath"/> from the segments in <paramref name="sessionDirectory"/>.
    /// <paramref name="lapLengthM"/> is supplied by the caller (Storage stays sim-agnostic). A session
    /// with no completed laps still produces a valid, empty-of-row-groups Parquet file. Returns the
    /// number of bounded laps skipped because their position is non-monotonic (a crash/spin/pit detour
    /// cannot be resampled) — those are dropped individually rather than failing the whole file.
    /// </summary>
    public static int Write(string sessionDirectory, float lapLengthM, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapLengthM);

        (IReadOnlyList<ResampledLap> laps, int skipped) = ResampleLaps(sessionDirectory, lapLengthM);
        WriteParquet(outputPath, laps);
        return skipped;
    }

    private static (IReadOnlyList<ResampledLap> Laps, int Skipped) ResampleLaps(
        string sessionDirectory, float lapLengthM)
    {
        LapSegmenter segmenter = new();
        List<ResampledLap> laps = [];
        int skipped = 0;
        foreach (TelemetryFrame frame in McapSegmentEnumerator.Read(sessionDirectory))
        {
            CompletedLap? completed = segmenter.Accept(frame);
            if (completed is null)
            {
                continue;
            }

            try
            {
                laps.Add(PositionResampler.Resample(completed.Frames, lapLengthM));
            }
            catch (ArgumentException)
            {
                // A crash/spin/pit lap whose position steps backward cannot be resampled. Skip just
                // that lap (compute already warns about it per-lap) instead of aborting the whole
                // file — one bad lap must not cost the session its other laps' parquet.
                skipped++;
            }
        }

        return (laps, skipped);
    }

    private static void WriteParquet(string outputPath, IReadOnlyList<ResampledLap> laps)
    {
        using var writer = new ParquetFileWriter(outputPath, ResampledLapParquet.BuildColumns());
        foreach (ResampledLap lap in laps)
        {
            ResampledLapParquet.WriteRowGroup(writer, lap);
        }

        // The `using` disposes the writer, which flushes the footer — no explicit Close() needed.
    }
}
