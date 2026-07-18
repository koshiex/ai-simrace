using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// Calibration oracle for the curvature-integral sustained-bend channel (ADR-0022 / OD-B1). It runs the real
/// production <see cref="CornerCenterlineDetector"/> on the vendored Monza/Spa owner centerlines — which carry
/// per-bin lateral g — in two modes, driven through the internal sustained-scale seam:
/// <list type="number">
/// <item>with g intact, the channel must preserve every owner-baked corner and not change the corner count
/// (no regression — the vendored maps must still bake the same); and</item>
/// <item>with g forced to 0 (the ghost-centerline case, ADR-0022), the channel must RECOVER the fast corners
/// (R &gt; <see cref="CornerCenterlineDetector.CornerRadiusThresholdM"/>) — Curva Grande, spa_t02, spa_t16 —
/// that a g=0 run WITHOUT the channel drops.</item>
/// </list>
/// These fast corners live in the owner maps only because real lateral g activated them, so they are exactly
/// the corners that vanish on a ghost centerline; W and SustainedScale are tuned against this oracle.
/// </summary>
public sealed class SustainedBendCalibrationTests
{
    // Catalog lap lengths of the two owner-baked calibration tracks (metres).
    private const float MonzaLapLengthM = 5793f;
    private const float SpaLapLengthM = 7004f;

    [Fact]
    public void Channel_preserves_every_owner_baked_corner_on_monza_with_lateral_g() =>
        AssertNoRegressionWithLateralG("monza", MonzaLapLengthM);

    [Fact]
    public void Channel_preserves_every_owner_baked_corner_on_spa_with_lateral_g() =>
        AssertNoRegressionWithLateralG("spa", SpaLapLengthM);

    [Fact]
    public void Zeroed_g_plus_channel_recovers_curva_grande_on_monza() =>
        AssertRecoversFastCornersAtZeroedG("monza", MonzaLapLengthM);

    [Fact]
    public void Zeroed_g_plus_channel_recovers_the_fast_corners_on_spa() =>
        AssertRecoversFastCornersAtZeroedG("spa", SpaLapLengthM);

    private static void AssertNoRegressionWithLateralG(string trackId, float lapLengthM)
    {
        MedianCenterline centerline = LoadOwnerCenterline(trackId, lapLengthM);
        IReadOnlyList<Corner> ownerCorners = LoadOwnerCorners(trackId, lapLengthM);

        IReadOnlyList<DetectedCorner> withChannel = DetectWithChannel(centerline);
        IReadOnlyList<DetectedCorner> withoutChannel = DetectWithoutChannel(centerline);

        foreach (Corner owner in ownerCorners)
        {
            withChannel.Should().Contain(
                c => Covers(c, owner.ApexPosition),
                "owner-baked corner {0} (apex {1:F3}) must still be detected with the channel on",
                owner.Id, owner.ApexPosition);
        }

        // The channel only ADDS activation for fast arcs; the split/merge topology is decided on the base
        // load, so with lateral g present the corner count must be identical to the pre-channel detector.
        withChannel.Should().HaveSameCount(withoutChannel);
    }

    private static void AssertRecoversFastCornersAtZeroedG(string trackId, float lapLengthM)
    {
        MedianCenterline zeroed = ZeroLateralG(LoadOwnerCenterline(trackId, lapLengthM));
        IReadOnlyList<Corner> fastCorners = LoadOwnerCorners(trackId, lapLengthM)
            .Where(c => c.ApexRadiusM > CornerCenterlineDetector.CornerRadiusThresholdM)
            .ToList();
        fastCorners.Should().NotBeEmpty("the calibration track has owner-baked fast corners to recover");

        IReadOnlyList<DetectedCorner> withChannel = DetectWithChannel(zeroed);
        IReadOnlyList<DetectedCorner> withoutChannel = DetectWithoutChannel(zeroed);

        foreach (Corner fast in fastCorners)
        {
            withoutChannel.Should().NotContain(
                c => Covers(c, fast.ApexPosition),
                "a g=0 run WITHOUT the channel drops fast corner {0} (apex {1:F3}, R {2:F0} m)",
                fast.Id, fast.ApexPosition, fast.ApexRadiusM);
            withChannel.Should().Contain(
                c => Covers(c, fast.ApexPosition),
                "the sustained channel must recover fast corner {0} (apex {1:F3}, R {2:F0} m) at zeroed g",
                fast.Id, fast.ApexPosition, fast.ApexRadiusM);
        }
    }

    private static bool Covers(DetectedCorner corner, float apexPosition) =>
        corner.StartPosition <= apexPosition && apexPosition <= corner.EndPosition;

    private static IReadOnlyList<DetectedCorner> DetectWithChannel(MedianCenterline centerline) =>
        CornerCenterlineDetector.Detect(centerline, [centerline], CornerCenterlineDetector.SustainedScale);

    private static IReadOnlyList<DetectedCorner> DetectWithoutChannel(MedianCenterline centerline) =>
        CornerCenterlineDetector.Detect(centerline, [centerline], 0f);

    private static MedianCenterline ZeroLateralG(MedianCenterline centerline) => centerline with
    {
        Bins = centerline.Bins.Select(bin => bin with { LateralG = 0f }).ToList(),
    };

    private static MedianCenterline LoadOwnerCenterline(string trackId, float lapLengthM)
    {
        bool resolved = CenterlineGeometryDataset.Load().TryGetCenterline(trackId, lapLengthM, out MedianCenterline? centerline);
        resolved.Should().BeTrue("the embedded {0} centerline must load for the calibration oracle", trackId);
        return centerline!;
    }

    private static IReadOnlyList<Corner> LoadOwnerCorners(string trackId, float lapLengthM)
    {
        bool resolved = CornerGeometryDataset.Load().TryGetCorners(trackId, lapLengthM, out IReadOnlyList<Corner> corners);
        resolved.Should().BeTrue("the embedded {0} owner-baked corner geometry must load", trackId);
        return corners;
    }
}
