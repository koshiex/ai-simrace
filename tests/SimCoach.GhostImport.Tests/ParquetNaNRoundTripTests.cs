using FluentAssertions;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// M5 (MEDIUM) — the GATING spike for the seam-mask carrier. The whole NaN-sentinel seam design (and
/// commit-18's "no migration / no new column" premise) rests on a NaN in <c>world_x</c>/<c>world_z</c>
/// surviving <see cref="ReferenceParquetCodec.Write"/> → <see cref="ReferenceParquetCodec.Read"/>.
/// ParquetSharp writes column min/max statistics by default and NaN-in-stats was unverified in this
/// codebase, so this test proves the round-trip BEFORE the mask is built on it. If it ever goes RED,
/// the seam carrier must fall back to the reserved out-of-bbox non-NaN sentinel honored by the same
/// caller-side guard — a known branch, not an unplanned <see cref="ResampledLapParquet"/> schema change.
/// </summary>
public sealed class ParquetNaNRoundTripTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "simcoach-ghost-nan-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Nan_in_world_x_and_z_survives_write_then_read()
    {
        ResampledLap lap = LineOnlyLapWithSeamNaN();
        string path = Path.Combine(_dir, "references", "monza_bmw_m4_gt3_dry-warm_alien_line.parquet");

        ReferenceParquetCodec.Write(lap, path);
        ResampledLap read = ReferenceParquetCodec.Read(path);

        // The masked seam bin (index 0) must round-trip as NaN in both world channels.
        float.IsNaN(read.WorldX[0]).Should().BeTrue("the seam sentinel must survive the parquet round-trip");
        float.IsNaN(read.WorldZ[0]).Should().BeTrue("the seam sentinel must survive the parquet round-trip");

        // The unmasked bins must round-trip their real coordinates untouched.
        read.WorldX[1].Should().Be(20f);
        read.WorldZ[1].Should().Be(15f);
        read.WorldX[2].Should().Be(30f);
        read.WorldZ[2].Should().Be(25f);

        // Position stays a real, spanning grid even for the masked bin.
        read.PositionNormalized.Should().Equal(0f, 0.5f, 1f);
    }

    private static ResampledLap LineOnlyLapWithSeamNaN() => new()
    {
        LapNumber = 1,
        GridLength = 3,
        PositionNormalized = [0f, 0.5f, 1f],
        TMsFromLapStart = new int[3],
        SpeedMps = new float[3],
        ThrottlePct = new float[3],
        BrakePct = new float[3],
        SteerRad = new float[3],
        Gear = new int[3],
        TyreTempFl = new float[3],
        TyreTempFr = new float[3],
        TyreTempRl = new float[3],
        TyreTempRr = new float[3],
        GLat = new float[3],
        GLong = new float[3],
        WorldX = [float.NaN, 20f, 30f],
        WorldY = new float[3],
        WorldZ = [float.NaN, 15f, 25f],
    };

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
