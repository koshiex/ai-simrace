using SimCoach.Storage;

namespace SimCoach.Reference.Tests;

/// <summary>
/// Builds LINE-only <see cref="ResampledLap"/> fixtures (position + world X/Z populated, every TIME/speed/
/// pedal channel zero) the way <c>CenterlineLineReference.Build</c> / the GhostImport alien writer do — the
/// shape the line-deviation kernels sample and the reference codec round-trips.
/// </summary>
internal static class LineOnlyLap
{
    /// <summary>A tiny 3-bin line (mid bin has real world coords the reader can assert on).</summary>
    public static ResampledLap ThreeBin() => Build([0.0f, 0.5f, 0.95f], [1f, 3f, 5f], [10f, 30f, 50f]);

    /// <summary>An N-bin quarter-circle world path over pn 0..1 (radius m), a plausible distinct alien line.</summary>
    public static ResampledLap Circle(int n, float radius)
    {
        float[] position = new float[n];
        float[] worldX = new float[n];
        float[] worldZ = new float[n];
        for (int k = 0; k < n; k++)
        {
            float pos = k / (float)(n - 1);
            float theta = MathF.PI / 2f * pos;
            position[k] = pos;
            worldX[k] = radius * MathF.Cos(theta);
            worldZ[k] = radius * MathF.Sin(theta);
        }

        return Build(position, worldX, worldZ);
    }

    private static ResampledLap Build(float[] position, float[] worldX, float[] worldZ)
    {
        int n = position.Length;
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
