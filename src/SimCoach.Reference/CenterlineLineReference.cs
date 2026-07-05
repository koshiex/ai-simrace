using SimCoach.Storage;

namespace SimCoach.Reference;

/// <summary>
/// Projects a baked median centerline (M38 LINE reference) into a world-path <see cref="ResampledLap"/> the
/// line-deviation kernels already know how to sample: <c>PositionNormalized = DistanceM / LapLengthM</c> with
/// the bins' world X/Z. Only the position + world channels are populated — the LINE kernels
/// (<see cref="GridMetrics.InterpWorldXZ"/> / <see cref="GridMetrics.InterpWorldTangent"/>) read nothing else —
/// so time/speed/etc. stay at their zero defaults. Distinct from the PB TIME reference (ADR-0019).
/// </summary>
internal static class CenterlineLineReference
{
    public static ResampledLap Build(MedianCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(centerline);
        int n = centerline.Bins.Count;
        float length = centerline.LapLengthM > 0f ? centerline.LapLengthM : 1f;
        float[] position = new float[n];
        float[] worldX = new float[n];
        float[] worldZ = new float[n];
        for (int k = 0; k < n; k++)
        {
            CenterlineBin bin = centerline.Bins[k];
            position[k] = Math.Clamp(bin.DistanceM / length, 0f, 1f);
            worldX[k] = bin.X;
            worldZ[k] = bin.Z;
        }

        return new ResampledLap
        {
            LapNumber = centerline.LapCount,
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
