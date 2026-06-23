using FluentAssertions;
using ParquetSharp;
using SimCoach.Contracts.V1;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Storage.Tests;

public sealed class LapParquetWriterTests : IDisposable
{
    private static readonly string[] _expectedColumns =
    [
        "lap_number", "t_ms_from_lap_start", "position_normalized", "speed_mps", "throttle_pct",
        "brake_pct", "steer_rad", "gear", "tyre_temp_fl", "tyre_temp_fr", "tyre_temp_rl",
        "tyre_temp_rr", "g_lat", "g_long", "world_x", "world_y", "world_z",
    ];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "simcoach-parquet-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Writes_one_row_group_per_interior_lap_with_the_expected_schema()
    {
        string parquet = WriteFixture();

        using var reader = new ParquetFileReader(parquet);
        reader.FileMetaData.NumColumns.Should().Be(_expectedColumns.Length);
        // 4 synthesized laps → 2 fully-bounded interior laps after the segmenter discards first/last.
        reader.FileMetaData.NumRowGroups.Should().Be(2);

        SchemaDescriptor schema = reader.FileMetaData.Schema;
        string[] names = [.. Enumerable.Range(0, reader.FileMetaData.NumColumns).Select(i => schema.Column(i).Name)];
        names.Should().Equal(_expectedColumns);
        reader.Close();
    }

    [Fact]
    public void Each_row_group_holds_one_lap_resampled_to_the_grid()
    {
        string parquet = WriteFixture();
        int gridLength = (int)MathF.Ceiling(SyntheticTracks.Spa.LapLengthM);

        using var reader = new ParquetFileReader(parquet);
        int[] lapNumbers = ReadDistinctLapNumbers(reader, gridLength);
        reader.Close();

        lapNumbers.Should().Equal(2, 3);
    }

    [Fact]
    public void Round_trips_world_coordinates_and_ascending_position()
    {
        string parquet = WriteFixture();

        using var reader = new ParquetFileReader(parquet);
        using RowGroupReader rowGroup = reader.RowGroup(0);
        int rows = (int)rowGroup.MetaData.NumRows;
        float[] position = ReadFloats(rowGroup, ColumnIndex("position_normalized"), rows);
        float[] worldX = ReadFloats(rowGroup, ColumnIndex("world_x"), rows);
        reader.Close();

        position.Should().BeInAscendingOrder();
        worldX.Max(MathF.Abs).Should().BeGreaterThan(1f);
    }

    [Fact]
    public void Writes_a_valid_schema_with_zero_row_groups_when_no_lap_completes()
    {
        // A single lap is partial (the segmenter discards first/last) → zero completed laps.
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 1);
        SegmentFixture.Write(_dir, frames, framesPerSegment: 150);
        string parquet = Path.Combine(_dir, "laps.parquet");

        LapParquetWriter.Write(_dir, SyntheticTracks.Spa.LapLengthM, parquet);

        using var reader = new ParquetFileReader(parquet);
        reader.FileMetaData.NumColumns.Should().Be(_expectedColumns.Length);
        reader.FileMetaData.NumRowGroups.Should().Be(0);
        reader.Close();
    }

    private string WriteFixture()
    {
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        SegmentFixture.Write(_dir, frames, framesPerSegment: 150);
        string parquet = Path.Combine(_dir, "laps.parquet");
        LapParquetWriter.Write(_dir, SyntheticTracks.Spa.LapLengthM, parquet);
        return parquet;
    }

    private static int[] ReadDistinctLapNumbers(ParquetFileReader reader, int expectedRowsPerGroup)
    {
        List<int> result = [];
        for (int g = 0; g < reader.FileMetaData.NumRowGroups; g++)
        {
            using RowGroupReader rowGroup = reader.RowGroup(g);
            int rows = (int)rowGroup.MetaData.NumRows;
            rows.Should().Be(expectedRowsPerGroup);
            using LogicalColumnReader<int> column = rowGroup.Column(ColumnIndex("lap_number")).LogicalReader<int>();
            int[] values = column.ReadAll(rows);
            values.Distinct().Should().ContainSingle();
            result.Add(values[0]);
        }

        return [.. result];
    }

    private static float[] ReadFloats(RowGroupReader rowGroup, int columnIndex, int rows)
    {
        using LogicalColumnReader<float> column = rowGroup.Column(columnIndex).LogicalReader<float>();
        return column.ReadAll(rows);
    }

    private static int ColumnIndex(string name) => Array.IndexOf(_expectedColumns, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
