using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CornerGeometryReviewPageTests
{
    [Fact]
    public void Renders_html_with_svg_and_every_corner_id()
    {
        MedianCenterline centerline = BuildCenterline();
        var document = CornerGeometryDocument.FromDetected(
            "monza", 100f, lapCount: 5,
            [new DetectedCorner
            {
                StartPosition = 0.4f,
                ApexPosition = 0.5f,
                EndPosition = 0.6f,
                ApexRadiusM = 25f,
                PeakLateralG = 1.4f,
                Trigger = CornerChannel.Both,
            }]);

        string html = CornerGeometryReviewPage.Render(document, centerline);

        html.Should().StartWith("<!doctype html>");
        html.Should().Contain("<svg");
        html.Should().Contain("<polyline");
        html.Should().Contain("monza_t01");
        html.Should().EndWith("</html>");
    }

    private static MedianCenterline BuildCenterline()
    {
        List<CenterlineBin> bins = new(100);
        for (int i = 0; i < 100; i++)
        {
            bins.Add(new CenterlineBin
            {
                DistanceM = i,
                X = i,
                Z = i is > 40 and < 60 ? 5f : 0f,
                LateralG = 0f,
                LapSamples = 5,
            });
        }

        return new MedianCenterline { TrackId = "monza", LapLengthM = 100f, LapCount = 5, Bins = bins };
    }
}
