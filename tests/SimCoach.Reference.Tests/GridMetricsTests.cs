using FluentAssertions;
using SimCoach.Storage;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class GridMetricsTests
{
    // A production-shaped grid exactly as PositionResampler builds it: PositionNormalized[k] = k / lapLengthM
    // with gridLength = ceil(lapLengthM), so the last stored position is < 1.0. This is the shape where the
    // old round(position*(gridLength-1)) inverse drifted by up to one sample near the lap end.
    private static ResampledLap ResamplerGrid(float lapLengthM)
    {
        int gridLength = (int)MathF.Ceiling(lapLengthM);
        float[] position = new float[gridLength];
        int[] tMs = new int[gridLength];
        for (int k = 0; k < gridLength; k++)
        {
            position[k] = k / lapLengthM;
            tMs[k] = k;   // 1 ms per 1 m sample — a monotonic cumulative
        }

        return new ResampledLap
        {
            LapNumber = 1,
            GridLength = gridLength,
            PositionNormalized = position,
            TMsFromLapStart = tMs,
            SpeedMps = new float[gridLength],
            ThrottlePct = new float[gridLength],
            BrakePct = new float[gridLength],
            SteerRad = new float[gridLength],
            Gear = new int[gridLength],
            TyreTempFl = new float[gridLength],
            TyreTempFr = new float[gridLength],
            TyreTempRl = new float[gridLength],
            TyreTempRr = new float[gridLength],
            GLat = new float[gridLength],
            GLong = new float[gridLength],
            WorldX = new float[gridLength],
            WorldY = new float[gridLength],
            WorldZ = new float[gridLength],
        };
    }

    [Theory]
    [InlineData(1000.6f)]
    [InlineData(5793.2f)]
    [InlineData(7004f)]
    public void Index_round_trips_every_resampler_grid_sample(float lapLengthM)
    {
        ResampledLap grid = ResamplerGrid(lapLengthM);

        // Index(PositionNormalized[k]) == k for every sample — the resampler-consistency the old
        // (gridLength-1) denominator broke near the lap end (where gridLength-1 < lapLengthM).
        for (int k = 0; k < grid.GridLength; k++)
        {
            GridMetrics.Index(grid, grid.PositionNormalized[k]).Should().Be(k);
        }
    }

    [Fact]
    public void Index_clamps_out_of_range_positions_to_the_grid()
    {
        ResampledLap grid = ResamplerGrid(1000.6f);
        GridMetrics.Index(grid, -0.5f).Should().Be(0);
        GridMetrics.Index(grid, 1.5f).Should().Be(grid.GridLength - 1);
    }

    [Fact]
    public void FracIndex_is_zero_on_a_degenerate_single_sample_grid()
    {
        ResampledLap grid = ResamplerGrid(1f); // gridLength = 1
        GridMetrics.FracIndex(grid, 0.5f).Should().Be(0d);
    }
}
