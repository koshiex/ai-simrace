using System.Text.Json;
using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CornerGeometryDocumentTests
{
    [Fact]
    public void FromDetected_assigns_positional_ids_in_order()
    {
        var document = CornerGeometryDocument.FromDetected(
            "spa", 7004f, lapCount: 4, [Corner(0.1f), Corner(0.5f), Corner(0.9f)]);

        document.Corners.Select(c => c.Id).Should().Equal("spa_t01", "spa_t02", "spa_t03");
        document.SchemaVersion.Should().Be(CornerGeometryDocument.CurrentSchemaVersion);
        document.TrackId.Should().Be("spa");
    }

    [Fact]
    public void Round_trips_through_system_text_json()
    {
        var document = CornerGeometryDocument.FromDetected(
            "monza", 5793f, lapCount: 6,
            [new DetectedCorner
            {
                StartPosition = 0.15f,
                ApexPosition = 0.17f,
                EndPosition = 0.19f,
                ApexRadiusM = 20f,
                PeakLateralG = 1.3f,
                Trigger = CornerChannel.Both,
            }],
            sourceRecording: "20260624-193240-243");

        string json = JsonSerializer.Serialize(document);
        CornerGeometryDocument? restored = JsonSerializer.Deserialize<CornerGeometryDocument>(json);

        restored.Should().NotBeNull();
        restored!.LapLengthM.Should().Be(5793f);
        restored.LapCount.Should().Be(6);
        restored.SourceRecording.Should().Be("20260624-193240-243");
        restored.Corners.Should().ContainSingle();
        restored.Corners[0].Id.Should().Be("monza_t01");
        restored.Corners[0].ApexPosition.Should().BeApproximately(0.17f, 1e-6f);
        restored.Corners[0].Trigger.Should().Be("Both");
    }

    private static DetectedCorner Corner(float apexPosition) => new()
    {
        StartPosition = apexPosition - 0.01f,
        ApexPosition = apexPosition,
        EndPosition = apexPosition + 0.01f,
        ApexRadiusM = 50f,
        PeakLateralG = 1.2f,
        Trigger = CornerChannel.Curvature,
    };
}
