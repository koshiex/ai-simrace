using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CornerCenterlineDetectorTests
{
    [Fact]
    public void Finds_a_tight_corner_by_curvature()
    {
        // Tight arc (peak R = 30 m) with no lateral-g signal: must be found on the curvature channel.
        MedianCenterline centerline = BuildCenterline(
            lengthM: 400, cornerCenterM: 200, halfWidthM: 40, peakRadiusM: 30f, peakLateralG: 0f, turnSign: 1f);

        IReadOnlyList<DetectedCorner> corners = CornerCenterlineDetector.Detect(centerline);

        corners.Should().ContainSingle();
        DetectedCorner corner = corners[0];
        (corner.ApexPosition * centerline.LapLengthM).Should().BeApproximately(200f, 15f);
        corner.ApexRadiusM.Should().BeLessThan(80f);
        corner.Trigger.Should().Be(CornerChannel.Curvature);
    }

    [Fact]
    public void Finds_a_flat_large_radius_corner_by_lateral_g_alone()
    {
        // Gentle arc (peak R = 250 m, below the curvature gate) but sustained 1.5 g: the Curva Grande case.
        MedianCenterline centerline = BuildCenterline(
            lengthM: 600, cornerCenterM: 300, halfWidthM: 80, peakRadiusM: 250f, peakLateralG: 1.5f, turnSign: -1f);

        IReadOnlyList<DetectedCorner> corners = CornerCenterlineDetector.Detect(centerline);

        corners.Should().ContainSingle();
        DetectedCorner corner = corners[0];
        (corner.ApexPosition * centerline.LapLengthM).Should().BeApproximately(300f, 15f);
        corner.ApexRadiusM.Should().BeGreaterThan(CornerCenterlineDetector.CornerRadiusThresholdM);
        corner.Trigger.Should().Be(CornerChannel.LateralG);
    }

    [Fact]
    public void Splits_a_close_chicane_into_two_corners()
    {
        // Two opposite arcs 60 m apart: the fusion gate merges them into one complex, the splitter must
        // separate them at the curvature sign-change / load valley between the two apexes.
        MedianCenterline centerline = BuildChicane(
            lengthM: 460, c1: 200, c2: 260, halfWidthM: 40, peakRadiusM: 40f, peakLateralG: 1.5f);

        IReadOnlyList<DetectedCorner> corners = CornerCenterlineDetector.Detect(centerline);

        corners.Should().HaveCount(2);
        (corners[0].ApexPosition * centerline.LapLengthM).Should().BeApproximately(200f, 15f);
        (corners[1].ApexPosition * centerline.LapLengthM).Should().BeApproximately(260f, 15f);
    }

    [Fact]
    public void Finds_no_corner_on_a_straight()
    {
        MedianCenterline centerline = BuildCenterline(
            lengthM: 400, cornerCenterM: 200, halfWidthM: 40, peakRadiusM: 100_000f, peakLateralG: 0f, turnSign: 1f);

        IReadOnlyList<DetectedCorner> corners = CornerCenterlineDetector.Detect(centerline);

        corners.Should().BeEmpty();
    }

    /// <summary>
    /// Synthesizes a centerline of a straight with a single planted corner: curvature follows a
    /// triangular hump peaking at <paramref name="cornerCenterM"/> (so the apex argmax is unambiguous),
    /// and lateral g follows the same profile scaled to <paramref name="peakLateralG"/>.
    /// </summary>
    private static MedianCenterline BuildCenterline(
        int lengthM, int cornerCenterM, int halfWidthM, float peakRadiusM, float peakLateralG, float turnSign)
    {
        float peakKappa = 1f / peakRadiusM;
        float heading = 0f;
        float x = 0f;
        float z = 0f;
        List<CenterlineBin> bins = new(lengthM);
        for (int i = 0; i < lengthM; i++)
        {
            float dist = MathF.Abs(i - cornerCenterM);
            float profile = MathF.Max(0f, 1f - (dist / halfWidthM));
            bins.Add(new CenterlineBin
            {
                DistanceM = i,
                X = x,
                Z = z,
                LateralG = peakLateralG * profile,
                LapSamples = 5,
            });

            heading += turnSign * peakKappa * profile;
            x += MathF.Cos(heading);
            z += MathF.Sin(heading);
        }

        return new MedianCenterline
        {
            TrackId = "test",
            LapLengthM = lengthM,
            LapCount = 5,
            Bins = bins,
        };
    }

    /// <summary>
    /// Synthesizes a chicane: two opposite-sign triangular curvature humps centered at
    /// <paramref name="c1"/> and <paramref name="c2"/>, each carrying lateral g, so the fusion gate sees
    /// one merged complex with a sign-change/load valley between the apexes.
    /// </summary>
    private static MedianCenterline BuildChicane(
        int lengthM, int c1, int c2, int halfWidthM, float peakRadiusM, float peakLateralG)
    {
        float peakKappa = 1f / peakRadiusM;
        float heading = 0f;
        float x = 0f;
        float z = 0f;
        List<CenterlineBin> bins = new(lengthM);
        for (int i = 0; i < lengthM; i++)
        {
            float p1 = MathF.Max(0f, 1f - (MathF.Abs(i - c1) / halfWidthM));
            float p2 = MathF.Max(0f, 1f - (MathF.Abs(i - c2) / halfWidthM));
            bins.Add(new CenterlineBin
            {
                DistanceM = i,
                X = x,
                Z = z,
                LateralG = peakLateralG * (p1 + p2),
                LapSamples = 5,
            });

            heading += peakKappa * (p1 - p2);
            x += MathF.Cos(heading);
            z += MathF.Sin(heading);
        }

        return new MedianCenterline
        {
            TrackId = "test",
            LapLengthM = lengthM,
            LapCount = 5,
            Bins = bins,
        };
    }
}
