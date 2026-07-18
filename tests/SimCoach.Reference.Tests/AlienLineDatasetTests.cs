using FluentAssertions;
using SimCoach.Reference;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class AlienLineDatasetTests
{
    [Fact]
    public void FromLaps_resolves_a_known_track()
    {
        ResampledLap line = LineOnlyLap(worldX: [10f, 11f], worldZ: [20f, 21f]);
        var dataset = AlienLineDataset.FromLaps(new Dictionary<string, ResampledLap> { ["monza"] = line });

        dataset.TryGetAlienLine("monza", out ResampledLap? resolved).Should().BeTrue();
        resolved.Should().BeSameAs(line);
    }

    [Fact]
    public void FromLaps_rejects_an_unknown_track()
    {
        var dataset = AlienLineDataset.FromLaps(
            new Dictionary<string, ResampledLap> { ["monza"] = LineOnlyLap([10f, 11f], [20f, 21f]) });

        dataset.TryGetAlienLine("spa", out ResampledLap? resolved).Should().BeFalse();
        resolved.Should().BeNull();
    }

    [Theory]
    [InlineData("monza")]
    [InlineData("spa")]
    public void Load_resolves_a_vendored_alien_line(string trackId)
    {
        // Each vendored alien LINE (a real accreplay GT3 lap, decoded + centerline-aligned + seam-masked) is
        // embedded as Data/alien_line.<track>.parquet, so Load() resolves it and ComputeSession prefers it over
        // the centerline. "spa" also guards the culture-inference fix: its id is a valid culture code, so the
        // asset only survives embedding with the csproj LogicalName/WithCulture pin.
        var dataset = AlienLineDataset.Load();

        dataset.TryGetAlienLine(trackId, out ResampledLap? line).Should().BeTrue();
        line.Should().NotBeNull();
        line!.GridLength.Should().BeGreaterThan(0);
        line.WorldX.Should().HaveCount(line.GridLength);
        line.SpeedMps.Should().OnlyContain(v => v == 0f); // LINE-only: alien never carries TIME/speed
    }

    [Fact]
    public void Load_falls_back_for_a_track_with_no_vendored_alien_line()
    {
        AlienLineDataset.Load().TryGetAlienLine("test_oval", out ResampledLap? unknown).Should().BeFalse();
        unknown.Should().BeNull();
    }

    [Fact]
    public void ReadEmbeddedLap_round_trips_a_line_only_parquet_stream()
    {
        ResampledLap line = LineOnlyLap(worldX: [10f, 11f, 12f], worldZ: [20f, 21f, 22f]);
        string path = Path.Combine(Path.GetTempPath(), $"alien_line_test_{Guid.NewGuid():N}.parquet");
        try
        {
            ReferenceParquetCodec.Write(line, path);
            using FileStream stream = File.OpenRead(path);

            ResampledLap read = AlienLineDataset.ReadEmbeddedLap(stream);

            read.GridLength.Should().Be(3);
            read.WorldX.Should().Equal(line.WorldX);
            read.WorldZ.Should().Equal(line.WorldZ);
            read.PositionNormalized.Should().Equal(line.PositionNormalized);
            read.SpeedMps.Should().OnlyContain(v => v == 0f);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static ResampledLap LineOnlyLap(float[] worldX, float[] worldZ)
    {
        int n = worldX.Length;
        float[] position = new float[n];
        for (int k = 0; k < n; k++)
        {
            position[k] = n > 1 ? (float)k / (n - 1) : 0f;
        }

        return new ResampledLap
        {
            LapNumber = 1,
            GridLength = n,
            PositionNormalized = position,
            TMsFromLapStart = new int[n],
            SpeedMps = new float[n],
            ThrottlePct = new float[n],
            BrakePct = new float[n],
            SteerRad = new float[n],
            Gear = new int[n],
            TyreTempFl = new float[n],
            TyreTempFr = new float[n],
            TyreTempRl = new float[n],
            TyreTempRr = new float[n],
            GLat = new float[n],
            GLong = new float[n],
            WorldX = worldX,
            WorldY = new float[n],
            WorldZ = worldZ,
        };
    }
}
