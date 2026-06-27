using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Reference;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CenterlineCoherenceTests
{
    private const float SpaLengthM = 7004f;

    [Fact]
    public void Clean_laps_pass_the_gate()
    {
        IReadOnlyList<IReadOnlyList<TelemetryFrame>> laps =
            SegmentLaps(SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 5));

        CoherenceReport report = CenterlineCoherence.Evaluate("spa", SpaLengthM, laps);

        report.LapCount.Should().Be(3);
        report.Go.Should().BeTrue();
        report.MedianDeviationM.Should().BeLessThan(CenterlineCoherence.MaxTrustedMedianDeviationM);
        report.Reasons.Should().BeEmpty();
    }

    [Fact]
    public void A_single_lap_teleport_lands_in_max_not_median_and_still_passes()
    {
        List<List<TelemetryFrame>> laps =
            [.. SegmentLaps(SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 5)).Select(l => l.ToList())];

        List<TelemetryFrame> victim = laps[0];
        int bin0Index = IndexOfNearestDistance(victim, distanceM: 0f);
        TelemetryFrame teleported = victim[bin0Index].Clone();
        teleported.WorldPos = new Vec3 { X = teleported.WorldPos.X + 300f, Y = 0f, Z = teleported.WorldPos.Z };
        victim[bin0Index] = teleported;

        CoherenceReport report = CenterlineCoherence.Evaluate(
            "spa", SpaLengthM, [.. laps.Select(l => (IReadOnlyList<TelemetryFrame>)l)]);

        report.Go.Should().BeTrue("the median-from-median rejects a single-lap teleport");
        report.MedianDeviationM.Should().BeLessThan(CenterlineCoherence.MaxTrustedMedianDeviationM);
        report.MaxDeviationM.Should().BeGreaterThan(100f, "the outlier still surfaces in worst-single-lap deviation");
    }

    [Fact]
    public void Too_few_laps_fail_closed()
    {
        IReadOnlyList<IReadOnlyList<TelemetryFrame>> laps =
            SegmentLaps(SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 3));

        CoherenceReport report = CenterlineCoherence.Evaluate("spa", SpaLengthM, laps);

        report.Go.Should().BeFalse();
        report.Reasons.Should().Contain(r => r.Contains("lap", StringComparison.Ordinal));
    }

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
