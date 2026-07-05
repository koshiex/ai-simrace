using System.Text.Json;
using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CenterlineGeometryDocumentTests
{
    [Fact]
    public void Round_trips_a_median_centerline_through_json()
    {
        MedianCenterline centerline = new()
        {
            TrackId = "monza",
            LapLengthM = 5793.4f,
            LapCount = 4,
            Bins =
            [
                new CenterlineBin { DistanceM = 0, X = 1.5f, Z = 2.5f, LateralG = 0.1f, LapSamples = 4 },
                new CenterlineBin { DistanceM = 1, X = 1.6f, Z = 2.7f, LateralG = 0.9f, LapSamples = 3 },
            ],
        };

        var doc = CenterlineGeometryDocument.FromCenterline(centerline, sourceRecording: "rec-1");
        string json = JsonSerializer.Serialize(doc);
        CenterlineGeometryDocument? read = JsonSerializer.Deserialize<CenterlineGeometryDocument>(json);

        read.Should().NotBeNull();
        read!.SchemaVersion.Should().Be(CenterlineGeometryDocument.CurrentSchemaVersion);
        read.SourceRecording.Should().Be("rec-1");
        read.ToCenterline().Should().BeEquivalentTo(centerline);
    }
}
