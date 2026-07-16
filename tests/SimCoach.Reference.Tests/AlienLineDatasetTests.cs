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

    [Fact]
    public void Load_is_inert_while_no_alien_line_is_vendored()
    {
        // Guards the scaffold contract: the embed glob matches nothing today, so Load() resolves no track and
        // callers fall back to the centerline / PB line. Flips the day a real asset is vendored.
        var dataset = AlienLineDataset.Load();

        dataset.TryGetAlienLine("monza", out ResampledLap? resolved).Should().BeFalse();
        resolved.Should().BeNull();
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
