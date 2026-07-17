using SimCoach.Reference;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// Synthetic, network-free geometry for the lap-split / align / resample tests: a circular "track"
/// centerline and circular ghost paths offset from it by a known lateral deviation. A circle is the
/// simplest closed loop that exercises loop-closure splitting (it leaves and returns to the start point)
/// and gives an analytically-known nearest-point deviation for the alignment ceiling guard. NEVER a real
/// <c>.ghost</c> — these bytes prove pipeline mechanics, not ACC format correctness.
/// </summary>
internal static class SyntheticTrackFixture
{
    internal const float Radius = 200f;
    internal const int Bins = 1200;
    internal const int PointsPerLap = 250;

    /// <summary>A circular centerline of <paramref name="bins"/> 1 m bins at <paramref name="radius"/>.</summary>
    internal static MedianCenterline CircleCenterline(
        string trackId = "monza", int bins = Bins, float radius = Radius)
    {
        var list = new CenterlineBin[bins];
        for (int k = 0; k < bins; k++)
        {
            double angle = 2d * Math.PI * k / bins;
            list[k] = new CenterlineBin
            {
                DistanceM = k,
                X = (float)(radius * Math.Cos(angle)),
                Z = (float)(radius * Math.Sin(angle)),
                LateralG = 0f,
                LapSamples = 10,
            };
        }

        return new MedianCenterline
        {
            TrackId = trackId,
            LapLengthM = bins,
            LapCount = 10,
            Bins = list,
        };
    }

    /// <summary>
    /// A circular ghost path of <paramref name="laps"/> loops sampled at <paramref name="pointsPerLap"/>
    /// per loop, offset to <paramref name="radius"/>. The path returns to its start point every loop so
    /// <see cref="LapSplitter"/> can close it; a lateral offset from the centerline radius drives the
    /// alignment deviation.
    /// </summary>
    internal static IReadOnlyList<GhostRecord> CircularGhost(
        int laps, float radius, int pointsPerLap = PointsPerLap)
    {
        int total = laps * pointsPerLap;
        var list = new List<GhostRecord>(total + 1);
        for (int i = 0; i <= total; i++)
        {
            double angle = 2d * Math.PI * i / pointsPerLap;
            list.Add(new GhostRecord(
                WorldX: (float)(radius * Math.Cos(angle)),
                WorldY: 0f,
                WorldZ: (float)(radius * Math.Sin(angle)),
                Yaw: 0f,
                BrakeNorm: 0f,
                ThrottleNorm: 0f,
                RawTimestamp: i));
        }

        return list;
    }
}
