using FluentAssertions;
using SimCoach.Reference;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// Seam validity mask emission (MUST-FIX #1 data-shape half / OD9 full suppression): bins in the
/// configured pn bands get the NaN world sentinel, everything else keeps real coordinates, and the
/// sentinel survives the reference parquet round-trip (proving the emitted grid is what the runtime
/// consumers — commit 22 — will later honor).
/// </summary>
public sealed class SeamMaskEmitTests : IDisposable
{
    private static readonly GhostImportOptions _options = new();

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "simcoach-ghost-seam-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Is_masked_matches_the_default_seam_bands_only()
    {
        SeamMask.IsMasked(0.00f, _options.SeamBands).Should().BeTrue();
        SeamMask.IsMasked(0.01f, _options.SeamBands).Should().BeTrue();
        SeamMask.IsMasked(0.50f, _options.SeamBands).Should().BeFalse();
        SeamMask.IsMasked(0.92f, _options.SeamBands).Should().BeTrue();
        SeamMask.IsMasked(1.00f, _options.SeamBands).Should().BeTrue();
    }

    [Fact]
    public void Apply_masks_only_seam_band_bins_and_preserves_position()
    {
        ResampledLap grid = LineLap(
            positions: [0f, 0.01f, 0.5f, 0.93f, 0.99f],
            worldX: [1f, 2f, 3f, 4f, 5f],
            worldZ: [10f, 20f, 30f, 40f, 50f]);

        ResampledLap masked = SeamMask.Apply(grid, _options.SeamBands);

        // Seam bins (pn 0, 0.01, 0.93, 0.99) go NaN; the mid-lap bin (0.5) keeps its real coordinates.
        float.IsNaN(masked.WorldX[0]).Should().BeTrue();
        float.IsNaN(masked.WorldX[1]).Should().BeTrue();
        masked.WorldX[2].Should().Be(3f);
        masked.WorldZ[2].Should().Be(30f);
        float.IsNaN(masked.WorldX[3]).Should().BeTrue();
        float.IsNaN(masked.WorldZ[4]).Should().BeTrue();

        // Position stays the true value so the grid still spans 0..1; the source lap is untouched (immutable).
        masked.PositionNormalized.Should().Equal(grid.PositionNormalized);
        float.IsNaN(grid.WorldX[0]).Should().BeFalse();
    }

    [Fact]
    public void Resampled_masked_grid_round_trips_the_seam_sentinel_through_the_reference_codec()
    {
        MedianCenterline centerline = SyntheticTrackFixture.CircleCenterline();
        IReadOnlyList<GhostRecord> lap = SyntheticTrackFixture.CircularGhost(laps: 1, radius: SyntheticTrackFixture.Radius + 1.5f);
        IReadOnlyList<AlignedPoint> aligned = CenterlineAligner.Align(lap, centerline, _options);
        ResampledLap grid = LineResampler.Resample(aligned, centerline.LapLengthM, lapNumber: 1, _options);
        ResampledLap masked = SeamMask.Apply(grid, _options.SeamBands);
        string path = Path.Combine(_dir, "references", "monza_bmw_m4_gt3_dry-warm_alien_line.parquet");

        ReferenceParquetCodec.Write(masked, path);
        ResampledLap read = ReferenceParquetCodec.Read(path);

        read.GridLength.Should().Be(masked.GridLength);
        for (int k = 0; k < read.GridLength; k++)
        {
            if (SeamMask.IsMasked(read.PositionNormalized[k], _options.SeamBands))
            {
                float.IsNaN(read.WorldX[k]).Should().BeTrue($"seam bin {k} must stay NaN after the round-trip");
                float.IsNaN(read.WorldZ[k]).Should().BeTrue($"seam bin {k} must stay NaN after the round-trip");
            }
            else
            {
                float.IsNaN(read.WorldX[k]).Should().BeFalse($"non-seam bin {k} must keep real coordinates");
            }
        }
    }

    private static ResampledLap LineLap(float[] positions, float[] worldX, float[] worldZ)
    {
        int n = positions.Length;
        return new ResampledLap
        {
            LapNumber = 1,
            GridLength = n,
            PositionNormalized = positions,
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

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
