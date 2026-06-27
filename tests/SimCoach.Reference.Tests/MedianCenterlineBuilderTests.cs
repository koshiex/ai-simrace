using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Reference;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class MedianCenterlineBuilderTests
{
    private const float SpaLengthM = 7004f;
    private static readonly float _radiusM = SpaLengthM / (2f * MathF.PI);

    [Fact]
    public void Aggregates_clean_laps_onto_the_synthetic_circle()
    {
        IReadOnlyList<IReadOnlyList<TelemetryFrame>> laps =
            SegmentLaps(SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 5));

        MedianCenterline centerline = MedianCenterlineBuilder.Build("spa", SpaLengthM, laps);

        centerline.LapCount.Should().Be(3); // 5 laps -> 3 fully bounded interior laps
        centerline.Bins.Should().NotBeEmpty();
        // The synthetic world path is a circle of radius L/2pi; every bin must land on it.
        centerline.Bins.Should().OnlyContain(b => OnCircle(b, tolerationM: 1f));
    }

    [Fact]
    public void Median_rejects_a_single_lap_bin0_teleport()
    {
        List<List<TelemetryFrame>> laps =
            [.. SegmentLaps(SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 5)).Select(l => l.ToList())];

        // Corrupt ONE lap's bin-0 frame with a 300 m world-position teleport.
        List<TelemetryFrame> victim = laps[0];
        int bin0Index = IndexOfNearestDistance(victim, distanceM: 0f);
        TelemetryFrame teleported = victim[bin0Index].Clone();
        teleported.WorldPos = new Vec3 { X = teleported.WorldPos.X + 300f, Y = 0f, Z = teleported.WorldPos.Z };
        victim[bin0Index] = teleported;

        MedianCenterline centerline = MedianCenterlineBuilder.Build(
            "spa", SpaLengthM, [.. laps.Select(l => (IReadOnlyList<TelemetryFrame>)l)]);

        // The median over 3 laps rejects the lone outlier — bin 0 stays on the circle.
        CenterlineBin bin0 = centerline.Bins.Single(b => b.DistanceM == 0);
        Radius(bin0).Should().BeApproximately(_radiusM, 1f);
    }

    [Fact]
    public void Reports_low_lap_count_without_meeting_the_trust_floor()
    {
        // 3 laps -> only 1 interior bounded lap, below MinLapsForTrust.
        IReadOnlyList<IReadOnlyList<TelemetryFrame>> laps =
            SegmentLaps(SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 3));

        MedianCenterline centerline = MedianCenterlineBuilder.Build("spa", SpaLengthM, laps);

        centerline.LapCount.Should().BeLessThan(MedianCenterlineBuilder.MinLapsForTrust);
    }

    private static bool OnCircle(CenterlineBin bin, float tolerationM) =>
        MathF.Abs(Radius(bin) - _radiusM) < tolerationM;

    private static float Radius(CenterlineBin bin) => MathF.Sqrt((bin.X * bin.X) + (bin.Z * bin.Z));

    private static int IndexOfNearestDistance(IReadOnlyList<TelemetryFrame> frames, float distanceM)
    {
        int best = 0;
        float bestDelta = float.MaxValue;
        for (int i = 0; i < frames.Count; i++)
        {
            float delta = MathF.Abs(frames[i].LapDistanceM - distanceM);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = i;
            }
        }

        return best;
    }

    private static IReadOnlyList<IReadOnlyList<TelemetryFrame>> SegmentLaps(IReadOnlyList<TelemetryFrame> frames)
    {
        LapSegmenter segmenter = new();
        List<IReadOnlyList<TelemetryFrame>> laps = [];
        foreach (TelemetryFrame frame in frames)
        {
            if (segmenter.Accept(frame) is { } lap)
            {
                laps.Add(lap.Frames);
            }
        }

        return laps;
    }
}
