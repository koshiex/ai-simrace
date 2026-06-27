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
    /// number of row groups written and the number of bounded laps skipped because their position is
    /// non-monotonic (a crash/spin/pit detour cannot be resampled) — those are dropped individually
    /// rather than failing the whole file. <c>Written + Skipped</c> is the bounded-lap count the replay
    /// path saw; the caller reconciles those lap numbers against the <c>laps</c> rows to detect a
    /// DB↔parquet desync (a value-level mismatch, not just a count mismatch).
    /// </summary>
    public static LapParquetWriteResult Write(string sessionDirectory, float lapLengthM, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapLengthM);

        (IReadOnlyList<ResampledLap> laps, IReadOnlyList<int> skippedLapNumbers) =
            ResampleLaps(sessionDirectory, lapLengthM);
        WriteParquet(outputPath, laps);
        return new LapParquetWriteResult([.. laps.Select(l => l.LapNumber)], skippedLapNumbers);
    }

    private static (IReadOnlyList<ResampledLap> Laps, IReadOnlyList<int> SkippedLapNumbers) ResampleLaps(
        string sessionDirectory, float lapLengthM)
    {
        LapSegmenter segmenter = new();
        List<ResampledLap> laps = [];
        List<int> skippedLapNumbers = [];
        foreach (TelemetryFrame frame in McapSegmentEnumerator.Read(sessionDirectory))
        {
            CompletedLap? completed = segmenter.Accept(frame);
            if (completed is null)
            {
                continue;
            }

            try
            {
                // Clamp non-monotonic (crash/spin) laps so they stay in the parquet for post-session
                // review rather than being dropped — they are is_clean = 0 and never become references.
                laps.Add(PositionResampler.Resample(
                    completed.Frames, lapLengthM, completed.LapNumber, clampNonMonotonic: true));
            }
            catch (ArgumentException)
            {
                // A degenerate lap (e.g. too few frames) still can't be resampled — skip just it,
                // never abort the whole file. Keep its lap number so the caller's reconciliation knows
                // this lap is legitimately absent from the parquet (it is still a row in the laps table).
                skippedLapNumbers.Add(completed.LapNumber);
            }
        }

        return (laps, skippedLapNumbers);
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
