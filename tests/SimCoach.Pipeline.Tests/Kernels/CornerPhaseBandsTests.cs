using FluentAssertions;
using SimCoach.Pipeline.Kernels;
using Xunit;

namespace SimCoach.Pipeline.Tests.Kernels;

public sealed class CornerPhaseBandsTests
{
    // Canonical corner: start 0.30, apex 0.40, end 0.50, fraction 0.25.
    // length 0.20, apexOffset 0.10 → apexStart 0.075, apexEnd 0.125, turnInStart 0.0375.
    [Fact]
    public void Offsets_match_the_apex_band_arithmetic_for_a_canonical_corner()
    {
        CornerPhaseOffsets offsets = CornerPhaseBands.Offsets(0.30, 0.40, 0.50, 0.25);

        offsets.Length.Should().BeApproximately(0.20, 1e-9);
        offsets.TurnInStart.Should().BeApproximately(0.0375, 1e-9);
        offsets.ApexStart.Should().BeApproximately(0.075, 1e-9);
        offsets.ApexEnd.Should().BeApproximately(0.125, 1e-9);
    }

    [Fact]
    public void Turn_in_to_apex_band_is_absolute_turn_in_start_through_apex_end()
    {
        (float lo, float hi) = CornerPhaseBands.TurnInToApexBand(0.30, 0.40, 0.50, 0.25);

        lo.Should().BeApproximately(0.3375f, 1e-5f);
        hi.Should().BeApproximately(0.425f, 1e-5f);
    }

    [Fact]
    public void Turn_in_start_is_half_the_apex_start_offset()
    {
        CornerPhaseOffsets offsets = CornerPhaseBands.Offsets(0.10, 0.15, 0.25, 0.25);

        offsets.TurnInStart.Should().BeApproximately(offsets.ApexStart / 2.0, 1e-9);
    }

    [Fact]
    public void A_degenerate_window_yields_an_empty_band_at_the_start()
    {
        CornerPhaseBands.Offsets(0.40, 0.40, 0.40, 0.25).Should().Be(default(CornerPhaseOffsets));

        (float lo, float hi) = CornerPhaseBands.TurnInToApexBand(0.40, 0.40, 0.40, 0.25);
        lo.Should().Be(0.40f);
        hi.Should().Be(0.40f);
    }

    [Fact]
    public void A_non_wrapping_band_stays_a_sub_range_of_start_end()
    {
        (float lo, float hi) = CornerPhaseBands.TurnInToApexBand(0.30, 0.40, 0.50, 0.25);

        lo.Should().BeGreaterThan(0.30f);
        hi.Should().BeLessThan(0.50f);
        lo.Should().BeLessThan(hi);
    }

    [Fact]
    public void A_wrapping_corner_returns_raw_non_wrapping_endpoints_a_documented_limitation()
    {
        // An S/F-straddling corner (start 0.95, apex 1.02, end 1.10). The band length/offsets fold correctly
        // via Mod1, but TurnInToApexBand returns raw `start + offset` to match the metric's non-wrapping frame
        // slicing — so Hi exceeds 1.0 instead of folding to ~0.04. This documents (not fixes) the divergence
        // from the live resolver, which folds with Mod1; ACC's S/F sits on a straight so no real corner wraps.
        (float lo, float hi) = CornerPhaseBands.TurnInToApexBand(0.95, 1.02, 1.10, 0.25);

        lo.Should().BeApproximately(0.97625f, 1e-4f);
        hi.Should().BeApproximately(1.04f, 1e-4f, "the raw non-wrapping endpoint runs past the S/F line rather than folding");
        hi.Should().BeGreaterThan(1.0f);
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(-0.25, 0.75)]
    [InlineData(0.5, 0.5)]
    public void Mod1_folds_a_raw_delta_into_the_forward_wrap(double value, double expected)
    {
        CornerPhaseBands.Mod1(value).Should().BeApproximately(expected, 1e-9);
    }
}
