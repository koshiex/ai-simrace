using SimCoach.Reference;

namespace SimCoach.GhostImport;

/// <summary>
/// Aligns a decoded ghost lap onto the vendored pb centerline for the triple (the deterministic
/// alignment target present for monza+spa; a runtime PB is not guaranteed at import time). Each ghost
/// sample keeps its own driven world XZ (the alien LINE) and borrows the arc-length parameter
/// (position_normalized) of the nearest centerline bin. A ghost that does not track the reference corridor
/// — a wrong decode or a foreign lap — is rejected fail-fast when the MEDIAN nearest-point deviation
/// exceeds <see cref="GhostImportOptions.AlignmentDeviationCeilingM"/> (OD5).
/// </summary>
internal static class CenterlineAligner
{
    internal static IReadOnlyList<AlignedPoint> Align(
        IReadOnlyList<GhostRecord> lap, MedianCenterline centerline, GhostImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentNullException.ThrowIfNull(centerline);
        ArgumentNullException.ThrowIfNull(options);
        if (centerline.Bins.Count == 0)
        {
            throw new InvalidDataException("centerline has no bins to align against");
        }

        float length = centerline.LapLengthM > 0f ? centerline.LapLengthM : 1f;
        var aligned = new AlignedPoint[lap.Count];
        float[] deviations = new float[lap.Count];
        for (int i = 0; i < lap.Count; i++)
        {
            GhostRecord record = lap[i];
            CenterlineBin nearest = NearestBin(centerline.Bins, record.WorldX, record.WorldZ, out float deviation);
            deviations[i] = deviation;
            float positionNormalized = Math.Clamp(nearest.DistanceM / length, 0f, 1f);
            aligned[i] = new AlignedPoint(positionNormalized, record.WorldX, record.WorldZ);
        }

        float median = Median(deviations);
        if (median > options.AlignmentDeviationCeilingM)
        {
            throw new InvalidDataException(
                $"ghost lap median alignment deviation {median:0.00} m exceeds the ceiling "
                + $"{options.AlignmentDeviationCeilingM:0.00} m — the ghost does not track the "
                + $"'{centerline.TrackId}' centerline (wrong decode or foreign lap)");
        }

        return aligned;
    }

    /// <summary>
    /// Projects each ghost record onto <paramref name="centerline"/> and returns its COMMON-axis arc-length
    /// (the nearest bin's <see cref="CenterlineBin.DistanceM"/>). This is the bootstrap-axis primitive
    /// (B1b): a provisional centerline re-parameterizes every ghost onto one shared 0..N axis so the median
    /// binner does not smear physically-offset points. Unlike <see cref="Align"/> it carries NO deviation
    /// ceiling — on ghost-derived tracks the 2 m guard is informational only (OD-B3 / ADR-0022): different
    /// alien drivers legitimately run different lines, and the real backstop is the downstream
    /// span-coherence + corner-layout calibration, not a per-frame owner-tuned envelope.
    /// </summary>
    internal static IReadOnlyList<float> ProjectDistancesM(
        IReadOnlyList<GhostRecord> lap, MedianCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentNullException.ThrowIfNull(centerline);
        if (centerline.Bins.Count == 0)
        {
            throw new InvalidDataException("centerline has no bins to project onto");
        }

        float[] distances = new float[lap.Count];
        for (int i = 0; i < lap.Count; i++)
        {
            CenterlineBin nearest = NearestBin(centerline.Bins, lap[i].WorldX, lap[i].WorldZ, out _);
            distances[i] = nearest.DistanceM;
        }

        return distances;
    }

    /// <summary>Median nearest-point deviation (metres) of the lap against the centerline — diagnostic.</summary>
    internal static float MedianDeviationM(IReadOnlyList<GhostRecord> lap, MedianCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentNullException.ThrowIfNull(centerline);
        float[] deviations = new float[lap.Count];
        for (int i = 0; i < lap.Count; i++)
        {
            NearestBin(centerline.Bins, lap[i].WorldX, lap[i].WorldZ, out deviations[i]);
        }

        return Median(deviations);
    }

    private static CenterlineBin NearestBin(
        IReadOnlyList<CenterlineBin> bins, float x, float z, out float deviation)
    {
        CenterlineBin best = bins[0];
        float bestSq = float.PositiveInfinity;
        foreach (CenterlineBin bin in bins)
        {
            float dx = bin.X - x;
            float dz = bin.Z - z;
            float distSq = (dx * dx) + (dz * dz);
            if (distSq < bestSq)
            {
                bestSq = distSq;
                best = bin;
            }
        }

        deviation = MathF.Sqrt(bestSq);
        return best;
    }

    private static float Median(float[] values)
    {
        if (values.Length == 0)
        {
            return 0f;
        }

        float[] sorted = [.. values];
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[mid]
            : (sorted[mid - 1] + sorted[mid]) / 2f;
    }
}
