using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// The B3 bake-corners core (<see cref="CornerGeometryBaker"/>) must turn a ghost-derived median centerline
/// into a well-formed <c>cornerGeometry.&lt;track&gt;.json</c> document. This test drives it with a synthetic
/// rounded-square centerline — four tight quarter-circle corners separated by long straights, with zero
/// lateral g (the ghost case) — and asserts the document is non-empty and well-formed: detection-order ids,
/// each apex inside its extent, positive finite apex radius, and the ghost-map degenerate fields
/// (Trigger=Curvature, PeakLateralG=0). NEVER a network fetch — pure geometry proves the bake mechanics.
/// </summary>
public sealed class CornerGeometryBakerTests
{
    private const string TrackId = "brands_hatch";

    [Fact]
    public void Bake_yields_a_non_empty_well_formed_document_from_a_multi_corner_centerline()
    {
        MedianCenterline centerline = RoundedSquareCenterline(TrackId);

        CornerGeometryDocument document = CornerGeometryBaker.Bake(centerline);

        document.SchemaVersion.Should().Be(CornerGeometryDocument.CurrentSchemaVersion);
        document.TrackId.Should().Be(TrackId);
        document.LapLengthM.Should().Be(centerline.LapLengthM);
        document.LapCount.Should().Be(centerline.LapCount);
        document.Corners.Should().HaveCountGreaterThanOrEqualTo(2, "a rounded square has four distinct corners");

        float previousStart = -1f;
        for (int i = 0; i < document.Corners.Count; i++)
        {
            CornerGeometryEntry corner = document.Corners[i];
            corner.Id.Should().Be($"{TrackId}_t{i + 1:00}");
            corner.StartPosition.Should().BeInRange(0f, 1f);
            corner.EndPosition.Should().BeInRange(0f, 1f);
            corner.ApexPosition.Should().BeGreaterThanOrEqualTo(corner.StartPosition);
            corner.ApexPosition.Should().BeLessThanOrEqualTo(corner.EndPosition);
            corner.ApexRadiusM.Should().BeGreaterThan(0f);
            corner.ApexRadiusM.Should().NotBe(float.PositiveInfinity);
            corner.PeakLateralG.Should().Be(0f, "a ghost centerline carries no lateral g");
            corner.Trigger.Should().Be(nameof(CornerChannel.Curvature));

            corner.StartPosition.Should().BeGreaterThan(previousStart, "corners are emitted in ascending order");
            previousStart = corner.StartPosition;
        }
    }

    /// <summary>
    /// A closed rounded-square centerline binned at 1 m: four 200 m straights joined by four 40 m-radius
    /// quarter-circle corners. Straights (near-zero curvature) exceed the detector's bridge gap so the four
    /// corners stay distinct; each corner's tight radius is well inside the curvature threshold. Lateral g is
    /// zero throughout, exactly as a ghost-derived centerline.
    /// </summary>
    private static MedianCenterline RoundedSquareCenterline(string trackId)
    {
        const float straight = 200f;
        const float radius = 40f;
        const float half = 100f;
        var points = new List<(float X, float Z)>();

        AddStraight(points, (-half, -(half + radius)), (half, -(half + radius)), straight);
        AddArc(points, centerX: half, centerZ: -half, radius, startDeg: -90f, endDeg: 0f);
        AddStraight(points, (half + radius, -half), (half + radius, half), straight);
        AddArc(points, centerX: half, centerZ: half, radius, startDeg: 0f, endDeg: 90f);
        AddStraight(points, (half, half + radius), (-half, half + radius), straight);
        AddArc(points, centerX: -half, centerZ: half, radius, startDeg: 90f, endDeg: 180f);
        AddStraight(points, (-(half + radius), half), (-(half + radius), -half), straight);
        AddArc(points, centerX: -half, centerZ: -half, radius, startDeg: 180f, endDeg: 270f);

        var bins = new CenterlineBin[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            bins[i] = new CenterlineBin
            {
                DistanceM = i,
                X = points[i].X,
                Z = points[i].Z,
                LateralG = 0f,
                LapSamples = 5,
            };
        }

        return new MedianCenterline
        {
            TrackId = trackId,
            LapLengthM = points.Count,
            LapCount = 5,
            Bins = bins,
        };
    }

    private static void AddStraight(
        List<(float X, float Z)> points, (float X, float Z) from, (float X, float Z) to, float length)
    {
        float dx = to.X - from.X;
        float dz = to.Z - from.Z;
        float dist = MathF.Sqrt((dx * dx) + (dz * dz));
        float ux = dx / dist;
        float uz = dz / dist;
        int steps = (int)MathF.Round(length);
        for (int s = 0; s < steps; s++)
        {
            points.Add((from.X + (ux * s), from.Z + (uz * s)));
        }
    }

    private static void AddArc(
        List<(float X, float Z)> points, float centerX, float centerZ, float radius, float startDeg, float endDeg)
    {
        float startRad = startDeg * MathF.PI / 180f;
        float sweepRad = (endDeg - startDeg) * MathF.PI / 180f;
        float arcLen = radius * MathF.Abs(sweepRad);
        int steps = (int)MathF.Round(arcLen);
        for (int s = 0; s < steps; s++)
        {
            float theta = startRad + (s / radius);
            points.Add((centerX + (radius * MathF.Cos(theta)), centerZ + (radius * MathF.Sin(theta))));
        }
    }
}
