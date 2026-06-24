using ParquetSharp;

namespace SimCoach.Storage;

/// <summary>
/// The single source of truth for the <see cref="ResampledLap"/> Parquet column schema and the
/// per-lap (one row group) read/write logic. Both <see cref="LapParquetWriter"/> (multi-lap
/// <c>laps.parquet</c>) and <see cref="ReferenceParquetCodec"/> (single-lap reference file) build on
/// this, so the column set and order can never drift between writer and reader. Column order is the
/// explicit <c>data-model.md</c> order (not protobuf field order) and includes <c>world_x/y/z</c> for
/// racing-line deviation.
/// </summary>
internal static class ResampledLapParquet
{
    /// <summary>Parquet columns in <c>data-model.md</c> order — must match the per-row-group I/O order.</summary>
    public static Column[] BuildColumns() =>
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

    /// <summary>Appends one lap as a single row group. <c>lap_number</c> is filled for every grid point.</summary>
    public static void WriteRowGroup(ParquetFileWriter writer, ResampledLap lap)
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

    /// <summary>Reads one row group back into a <see cref="ResampledLap"/>.</summary>
    public static ResampledLap ReadRowGroup(RowGroupReader rowGroup)
    {
        int rows = checked((int)rowGroup.MetaData.NumRows);
        int[] lapNumbers = ReadInts(rowGroup, 0, rows);

        return new ResampledLap
        {
            LapNumber = rows > 0 ? lapNumbers[0] : 0,
            GridLength = rows,
            TMsFromLapStart = ReadInts(rowGroup, 1, rows),
            PositionNormalized = ReadFloats(rowGroup, 2, rows),
            SpeedMps = ReadFloats(rowGroup, 3, rows),
            ThrottlePct = ReadFloats(rowGroup, 4, rows),
            BrakePct = ReadFloats(rowGroup, 5, rows),
            SteerRad = ReadFloats(rowGroup, 6, rows),
            Gear = ReadInts(rowGroup, 7, rows),
            TyreTempFl = ReadFloats(rowGroup, 8, rows),
            TyreTempFr = ReadFloats(rowGroup, 9, rows),
            TyreTempRl = ReadFloats(rowGroup, 10, rows),
            TyreTempRr = ReadFloats(rowGroup, 11, rows),
            GLat = ReadFloats(rowGroup, 12, rows),
            GLong = ReadFloats(rowGroup, 13, rows),
            WorldX = ReadFloats(rowGroup, 14, rows),
            WorldY = ReadFloats(rowGroup, 15, rows),
            WorldZ = ReadFloats(rowGroup, 16, rows),
        };
    }

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

    private static float[] ReadFloats(RowGroupReader rowGroup, int columnIndex, int rows)
    {
        using LogicalColumnReader<float> column = rowGroup.Column(columnIndex).LogicalReader<float>();
        return column.ReadAll(rows);
    }

    private static int[] ReadInts(RowGroupReader rowGroup, int columnIndex, int rows)
    {
        using LogicalColumnReader<int> column = rowGroup.Column(columnIndex).LogicalReader<int>();
        return column.ReadAll(rows);
    }
}
