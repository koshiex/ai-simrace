using SimCoach.Storage;

namespace SimCoach.GhostImport;

/// <summary>
/// Applies the seam validity mask (MUST-FIX #1 data-shape half, OD9). Bins whose
/// <see cref="ResampledLap.PositionNormalized"/> falls inside a configured <see cref="SeamBand"/> get their
/// <see cref="ResampledLap.WorldX"/>/<see cref="ResampledLap.WorldZ"/> replaced by the NaN validity
/// sentinel; <see cref="ResampledLap.PositionNormalized"/> keeps its true value so the grid still spans
/// 0..1. The sentinel is the natural sibling of the existing <c>(0,0,0)</c> torn-frame sentinel and needs
/// no parquet-schema change (its round-trip is proven by <c>ParquetNaNRoundTripTests</c>). The runtime
/// LINE consumers honor the mask caller-side (commit 22): a masked seam contributes no racing-line cue,
/// silencing the single-ghost noise at the start-finish loop-closure artifact and the end-of-lap seam.
/// </summary>
internal static class SeamMask
{
    /// <summary>The per-bin validity sentinel written into masked world cells (M5-proven to round-trip).</summary>
    internal const float InvalidSentinel = float.NaN;

    /// <summary>Returns a new lap with seam-band bins' world XZ replaced by the sentinel (immutable copy).</summary>
    internal static ResampledLap Apply(ResampledLap grid, IReadOnlyList<SeamBand> seamBands)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(seamBands);

        float[] worldX = [.. grid.WorldX];
        float[] worldZ = [.. grid.WorldZ];
        for (int k = 0; k < grid.GridLength; k++)
        {
            if (IsMasked(grid.PositionNormalized[k], seamBands))
            {
                worldX[k] = InvalidSentinel;
                worldZ[k] = InvalidSentinel;
            }
        }

        return grid with { WorldX = worldX, WorldZ = worldZ };
    }

    /// <summary>True when <paramref name="positionNormalized"/> falls inside any configured seam band.</summary>
    internal static bool IsMasked(float positionNormalized, IReadOnlyList<SeamBand> seamBands)
    {
        for (int i = 0; i < seamBands.Count; i++)
        {
            if (seamBands[i].Contains(positionNormalized))
            {
                return true;
            }
        }

        return false;
    }
}
