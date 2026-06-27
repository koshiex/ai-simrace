using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CornerGeometryDatasetTests
{
    [Fact]
    public void Loads_the_embedded_monza_geometry()
    {
        var dataset = CornerGeometryDataset.Load();

        bool resolved = dataset.TryGetCorners("monza", 5793f, out IReadOnlyList<Corner> corners);

        resolved.Should().BeTrue();
        corners.Should().HaveCount(11);
        corners[0].Id.Should().Be("monza_t01");
        corners.Should().OnlyContain(c => c.StartPosition < c.EndPosition && c.EndPosition <= 1f);
    }

    [Fact]
    public void Returns_false_for_an_uncovered_track()
    {
        var dataset = CornerGeometryDataset.Load();

        dataset.TryGetCorners("silverstone", 5891f, out IReadOnlyList<Corner> corners).Should().BeFalse();
        corners.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_a_lap_length_mismatch()
    {
        var dataset = CornerGeometryDataset.Load();

        dataset.TryGetCorners("monza", 9999f, out _).Should().BeFalse();
    }

    [Fact]
    public void Disqualifies_a_track_with_an_out_of_range_corner()
    {
        var dataset = CornerGeometryDataset.FromDocuments([OutOfRangeDocument()]);

        dataset.TryGetCorners("test", 1000f, out IReadOnlyList<Corner> corners).Should().BeFalse();
        corners.Should().BeEmpty();
    }

    private static CornerGeometryDocument OutOfRangeDocument()
    {
        return new CornerGeometryDocument
        {
            SchemaVersion = CornerGeometryDocument.CurrentSchemaVersion,
            TrackId = "test",
            LapLengthM = 1000f,
            LapCount = 5,
            Corners =
            [
                new CornerGeometryEntry
                {
                    Id = "test_t01",
                    StartPosition = 0.5f,
                    ApexPosition = 0.6f,
                    EndPosition = 0.4f, // end < start -> out of range
                    ApexRadiusM = 20f,
                    PeakLateralG = 1f,
                    Trigger = "Both",
                },
            ],
        };
    }
}
