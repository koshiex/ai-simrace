using SimCoach.Storage;

namespace SimCoach.GhostImport;

/// <summary>
/// Resamples an aligned ghost lap onto a uniform per-metre position grid and projects it into a LINE-only
/// <see cref="ResampledLap"/>, EXACTLY like <c>CenterlineLineReference.Build</c>: only
/// <see cref="ResampledLap.PositionNormalized"/> / <see cref="ResampledLap.WorldX"/> /
/// <see cref="ResampledLap.WorldZ"/> (plus <c>LapNumber</c>/<c>GridLength</c>) are populated; every other
/// channel stays at its zero default (the ghost clock is logarithmic and its derived speed is untrustworthy —
/// alien references are LINE-only, never TIME). The grid mapping mirrors the runtime resampler
/// (<c>PositionNormalized[k] = k·step / lapLengthM</c>) so <c>GridMetrics</c> samples it identically.
/// </summary>
internal static class LineResampler
{
    internal static ResampledLap Resample(
        IReadOnlyList<AlignedPoint> alignedPoints, float lapLengthM, int lapNumber, GhostImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(alignedPoints);
        ArgumentNullException.ThrowIfNull(options);
        if (alignedPoints.Count == 0)
        {
            throw new InvalidDataException("cannot resample an empty aligned ghost lap");
        }

        float length = lapLengthM > 0f ? lapLengthM : 1f;
        float step = options.ResampleStepM > 0f ? options.ResampleStepM : 1f;
        int n = Math.Max(2, (int)MathF.Ceiling(length / step));

        AlignedPoint[] sorted = [.. alignedPoints];
        Array.Sort(sorted, static (a, b) => a.PositionNormalized.CompareTo(b.PositionNormalized));

        float[] position = new float[n];
        float[] worldX = new float[n];
        float[] worldZ = new float[n];
        int j = 0;
        for (int k = 0; k < n; k++)
        {
            float distance = MathF.Min(k * step, length);
            float t = Math.Clamp(distance / length, 0f, 1f);
            position[k] = t;

            while (j + 1 < sorted.Length && sorted[j + 1].PositionNormalized <= t)
            {
                j++;
            }

            (worldX[k], worldZ[k]) = SampleAt(sorted, j, t);
        }

        return new ResampledLap
        {
            LapNumber = lapNumber,
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

    private static (float X, float Z) SampleAt(AlignedPoint[] sorted, int j, float t)
    {
        if (t <= sorted[0].PositionNormalized)
        {
            return (sorted[0].WorldX, sorted[0].WorldZ);
        }

        if (j + 1 >= sorted.Length)
        {
            AlignedPoint last = sorted[^1];
            return (last.WorldX, last.WorldZ);
        }

        AlignedPoint lower = sorted[j];
        AlignedPoint upper = sorted[j + 1];
        float span = upper.PositionNormalized - lower.PositionNormalized;
        float f = span > 1e-9f ? (t - lower.PositionNormalized) / span : 0f;
        return (Lerp(lower.WorldX, upper.WorldX, f), Lerp(lower.WorldZ, upper.WorldZ, f));
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
}
