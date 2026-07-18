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
        corners.Should().NotBeEmpty(); // exact count is a detector/re-bake concern, not the loader's
        corners[0].Id.Should().Be("monza_t01");
        corners.Should().OnlyContain(c => c.StartPosition < c.EndPosition && c.EndPosition <= 1f);
    }

    [Fact]
    public void Returns_false_for_an_uncovered_track()
    {
        var dataset = CornerGeometryDataset.Load();

        dataset.TryGetCorners("test_oval", 5891f, out IReadOnlyList<Corner> corners).Should().BeFalse();
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

    [Fact]
    public void Carries_apex_radius_and_trigger_into_the_corner_model()
    {
        var dataset = CornerGeometryDataset.FromDocuments([RadiusDocument()]);

        dataset.TryGetCorners("test", 1000f, out IReadOnlyList<Corner> corners).Should().BeTrue();
        corners.Should().ContainSingle();
        corners[0].ApexRadiusM.Should().Be(42f);
        corners[0].Trigger.Should().Be("LateralG");
    }

    private static CornerGeometryDocument RadiusDocument() => new()
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
                StartPosition = 0.30f,
                ApexPosition = 0.40f,
                EndPosition = 0.50f,
                ApexRadiusM = 42f,
                PeakLateralG = 1.2f,
                Trigger = "LateralG",
            },
        ],
    };

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
