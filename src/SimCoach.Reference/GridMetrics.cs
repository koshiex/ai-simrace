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
    /// <summary>
    /// Fractional grid index for a normalized position, consistent with the resampler's own mapping.
    /// <see cref="PositionResampler"/> writes <c>PositionNormalized[k] = k / lapLengthM</c> with
    /// <c>gridLength = ceil(lapLengthM)</c>, so the inverse must divide by <c>lapLengthM</c>, NOT
    /// <c>gridLength - 1</c> — the latter is smaller than <c>lapLengthM</c> and drifts the index by up to
    /// one sample near the lap end. The effective length is recovered from the last stored sample
    /// (<c>PositionNormalized[gridLength-1] = (gridLength-1)/lapLengthM</c>), so the round-trip
    /// <c>Index(PositionNormalized[k]) == k</c> holds for the exact grid the resampler produced.
    /// </summary>
    public static double FracIndex(ResampledLap grid, float position)
    {
        int last = grid.GridLength - 1;
        if (last <= 0)
        {
            return 0d;
        }

        float lastPos = grid.PositionNormalized[last];
        double effectiveLength = lastPos > 0f ? last / (double)lastPos : last;
        double index = Math.Clamp(position, 0f, 1f) * effectiveLength;
        return Math.Clamp(index, 0d, last);
    }

    /// <summary>Nearest grid index for a normalized position, clamped to the grid.</summary>
    public static int Index(ResampledLap grid, float position) => (int)Math.Round(FracIndex(grid, position));

    /// <summary>
    /// Cumulative lap time (ms from lap start) at a normalized position. Nearest-index lookup: the single
    /// caller (sector delta, <c>ComputeSession</c>) reads a running cumulative that changes by ~1 ms per 1 m
    /// sample, so sub-sample interpolation buys nothing measurable — the denominator unification (via
    /// <see cref="Index"/>) is the correctness fix, not interpolation.
    /// </summary>
    public static int TimeAt(ResampledLap grid, float position) =>
        grid.GridLength == 0 ? 0 : grid.TMsFromLapStart[Index(grid, position)];

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

        double f = FracIndex(grid, position);
        int i0 = Math.Clamp((int)Math.Floor(f), 0, grid.GridLength - 2);
        int i1 = i0 + 1;
        float t = (float)(f - i0);
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
