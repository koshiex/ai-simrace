using SimCoach.Contracts.V1;
using SimCoach.Storage;

namespace SimCoach.Reference;

/// <summary>
/// Position-grid helpers shared by corner/sector delta computation. A reference lap is a 1 m
/// <see cref="ResampledLap"/>; these map a normalized lap position (0..1) onto that grid for
/// time-at-position deltas, world-coordinate interpolation (racing line), and corner-window slicing
/// (so the C4 kernels can run over the reference exactly as they do over the self lap).
/// </summary>
internal static class GridMetrics
{
    /// <summary>Nearest grid index for a normalized position, clamped to the grid.</summary>
    public static int Index(float position, int gridLength)
    {
        if (gridLength <= 1)
        {
            return 0;
        }

        int i = (int)MathF.Round(Math.Clamp(position, 0f, 1f) * (gridLength - 1));
        return Math.Clamp(i, 0, gridLength - 1);
    }

    /// <summary>Cumulative lap time (ms from lap start) at a normalized position.</summary>
    public static int TimeAt(ResampledLap grid, float position) =>
        grid.GridLength == 0 ? 0 : grid.TMsFromLapStart[Index(position, grid.GridLength)];

    /// <summary>Linearly-interpolated world (X, Z) at a normalized position (Y is vertical, unused).</summary>
    public static (float X, float Z) InterpWorldXZ(ResampledLap grid, float position)
    {
        if (grid.GridLength == 0)
        {
            return (0f, 0f);
        }

        if (grid.GridLength == 1)
        {
            return (grid.WorldX[0], grid.WorldZ[0]);
        }

        float f = Math.Clamp(position, 0f, 1f) * (grid.GridLength - 1);
        int i0 = Math.Clamp((int)MathF.Floor(f), 0, grid.GridLength - 2);
        int i1 = i0 + 1;
        float t = f - i0;
        return (Lerp(grid.WorldX[i0], grid.WorldX[i1], t), Lerp(grid.WorldZ[i0], grid.WorldZ[i1], t));
    }

    /// <summary>
    /// Reconstructs minimal frames over a grid index range so the C4 kernels (brake/throttle/min-speed)
    /// run on the reference. Balance is not reconstructed (no wheel-slip in the grid) — it is a
    /// self-only score on the <see cref="CornerEvent"/>.
    /// </summary>
    public static IReadOnlyList<TelemetryFrame> SliceToFrames(ResampledLap grid, int k0, int k1)
    {
        List<TelemetryFrame> frames = [];
        for (int k = k0; k <= k1 && k < grid.GridLength; k++)
        {
            frames.Add(new TelemetryFrame
            {
                NormalizedCarPosition = grid.PositionNormalized[k],
                SpeedMps = grid.SpeedMps[k],
                BrakePct = grid.BrakePct[k],
                ThrottlePct = grid.ThrottlePct[k],
                SteerRad = grid.SteerRad[k],
            });
        }

        return frames;
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
