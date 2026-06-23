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
    /// with no completed laps still produces a valid, empty-of-row-groups Parquet file.
    /// </summary>
    public static void Write(string sessionDirectory, float lapLengthM, string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapLengthM);

        IReadOnlyList<ResampledLap> laps = ResampleLaps(sessionDirectory, lapLengthM);
        WriteParquet(outputPath, laps);
    }

    private static IReadOnlyList<ResampledLap> ResampleLaps(string sessionDirectory, float lapLengthM)
    {
        LapSegmenter segmenter = new();
        List<ResampledLap> laps = [];
        foreach (TelemetryFrame frame in McapSegmentEnumerator.Read(sessionDirectory))
        {
            CompletedLap? completed = segmenter.Accept(frame);
            if (completed is not null)
            {
                laps.Add(PositionResampler.Resample(completed.Frames, lapLengthM));
            }
        }

        return laps;
    }

    private static void WriteParquet(string outputPath, IReadOnlyList<ResampledLap> laps)
    {
        using var writer = new ParquetFileWriter(outputPath, BuildColumns());
        foreach (ResampledLap lap in laps)
        {
            int[] lapNumbers = new int[lap.GridLength];
            Array.Fill(lapNumbers, lap.LapNumber);

            using RowGroupWriter rowGroup = writer.AppendRowGroup();
            WriteInts(rowGroup, lapNumbers);
            WriteInts(rowGroup, lap.TMsFromLapStart);
            WriteFloats(rowGroup, lap.PositionNormalized);
            WriteFloats(rowGroup, lap.SpeedMps);
            WriteFloats(rowGroup, lap.ThrottlePct);
            WriteFloats(rowGroup, lap.BrakePct);
            WriteFloats(rowGroup, lap.SteerRad);
            WriteInts(rowGroup, lap.Gear);
            WriteFloats(rowGroup, lap.TyreTempFl);
            WriteFloats(rowGroup, lap.TyreTempFr);
            WriteFloats(rowGroup, lap.TyreTempRl);
            WriteFloats(rowGroup, lap.TyreTempRr);
            WriteFloats(rowGroup, lap.GLat);
            WriteFloats(rowGroup, lap.GLong);
            WriteFloats(rowGroup, lap.WorldX);
            WriteFloats(rowGroup, lap.WorldY);
            WriteFloats(rowGroup, lap.WorldZ);
        }

        writer.Close();
    }

    /// <summary>Parquet columns in <c>data-model.md</c> order — must match the per-row-group write order.</summary>
    private static Column[] BuildColumns() =>
    [
        new Column<int>("lap_number"),
        new Column<int>("t_ms_from_lap_start"),
        new Column<float>("position_normalized"),
        new Column<float>("speed_mps"),
        new Column<float>("throttle_pct"),
        new Column<float>("brake_pct"),
        new Column<float>("steer_rad"),
        new Column<int>("gear"),
        new Column<float>("tyre_temp_fl"),
        new Column<float>("tyre_temp_fr"),
        new Column<float>("tyre_temp_rl"),
        new Column<float>("tyre_temp_rr"),
        new Column<float>("g_lat"),
        new Column<float>("g_long"),
        new Column<float>("world_x"),
        new Column<float>("world_y"),
        new Column<float>("world_z"),
    ];

    private static void WriteFloats(RowGroupWriter rowGroup, float[] values)
    {
        using LogicalColumnWriter<float> column = rowGroup.NextColumn().LogicalWriter<float>();
        column.WriteBatch(values);
    }

    private static void WriteInts(RowGroupWriter rowGroup, int[] values)
    {
        using LogicalColumnWriter<int> column = rowGroup.NextColumn().LogicalWriter<int>();
        column.WriteBatch(values);
    }
}
