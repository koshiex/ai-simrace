using FluentAssertions;
using Xunit;

namespace SimCoach.Storage.Tests;

public sealed class ReferenceParquetCodecTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "simcoach-refcodec-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_then_read_round_trips_every_column()
    {
        ResampledLap lap = SampleLap();
        string path = Path.Combine(_dir, "references", "spa_synthetic_gt3_dry-warm.parquet");

        ReferenceParquetCodec.Write(lap, path);
        ResampledLap read = ReferenceParquetCodec.Read(path);

        // World coordinates and tyre temps must survive — racing-line deviation depends on world_x/z.
        read.Should().BeEquivalentTo(lap);
    }

    [Fact]
    public void Write_creates_the_parent_directory()
    {
        string path = Path.Combine(_dir, "nested", "deeper", "ref.parquet");

        ReferenceParquetCodec.Write(SampleLap(), path);

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public void Read_missing_file_throws()
    {
        Action read = () => ReferenceParquetCodec.Read(Path.Combine(_dir, "absent.parquet"));

        read.Should().Throw<FileNotFoundException>();
    }

    private static ResampledLap SampleLap() => new()
    {
        LapNumber = 2,
        GridLength = 3,
        PositionNormalized = [0f, 0.5f, 1f],
        TMsFromLapStart = [0, 1000, 2000],
        SpeedMps = [70f, 22f, 68f],
        ThrottlePct = [1f, 0f, 0.9f],
        BrakePct = [0f, 0.9f, 0f],
        SteerRad = [0f, 0.4f, 0.1f],
        Gear = [4, 2, 4],
        TyreTempFl = [80f, 81f, 82f],
        TyreTempFr = [83f, 84f, 85f],
        TyreTempRl = [86f, 87f, 88f],
        TyreTempRr = [89f, 90f, 91f],
        GLat = [0.1f, 1.2f, 0.3f],
        GLong = [-0.2f, -1.1f, 0.4f],
        WorldX = [10f, 20f, 30f],
        WorldY = [0f, 0f, 0f],
        WorldZ = [5f, 15f, 25f],
    };

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
