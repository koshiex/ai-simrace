using FluentAssertions;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class ComputeOptionsTests
{
    [Fact]
    public void EnsureValid_passes_for_defaults()
    {
        var options = new ComputeOptions();

        Action act = options.EnsureValid;

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValid_throws_on_negative_brake_window_upstream()
    {
        // M16: the upstream distance is a metric length; a negative value would arm the tracker after the
        // corner start and silently defeat the brake-onset scan, so it must be rejected up front.
        var options = new ComputeOptions { BrakeWindowUpstreamM = -1f };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*BrakeWindowUpstreamM*");
    }

    [Fact]
    public void EnsureValid_allows_zero_brake_window_upstream()
    {
        var options = new ComputeOptions { BrakeWindowUpstreamM = 0f };

        Action act = options.EnsureValid;

        act.Should().NotThrow("zero disables the widening but is a valid configuration");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnsureValid_throws_on_non_positive_corner_ceiling(int ceiling)
    {
        // M3 Tier A: a zero/negative ceiling would neutralise every corner loss, silencing all coaching.
        var options = new ComputeOptions { MaxPlausibleCornerLossMs = ceiling };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxPlausibleCornerLossMs*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnsureValid_throws_on_non_positive_sector_ceiling(int ceiling)
    {
        var options = new ComputeOptions { MaxPlausibleSectorLossMs = ceiling };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxPlausibleSectorLossMs*");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-0.5f)]
    public void EnsureValid_throws_on_non_positive_deficit_ratio(float ratio)
    {
        // M3 Tier B: a zero/negative ratio would collapse the deficit budget to the floor everywhere.
        var options = new ComputeOptions { LapDeficitLossRatio = ratio };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*LapDeficitLossRatio*");
    }

    [Fact]
    public void EnsureValid_throws_on_negative_deficit_floor()
    {
        var options = new ComputeOptions { LapDeficitFloorMs = -1 };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*LapDeficitFloorMs*");
    }

    [Fact]
    public void EnsureValid_allows_zero_deficit_floor()
    {
        var options = new ComputeOptions { LapDeficitFloorMs = 0 };

        Action act = options.EnsureValid;

        act.Should().NotThrow("a zero floor is valid — it just removes the near-zero-deficit cushion");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(0.6)]
    public void EnsureValid_throws_on_out_of_range_apex_window_fraction(double fraction)
    {
        // M9: the shared apex-band fraction must mirror RuleEngineOptions' (0, 0.5] range so the metric
        // and the live gate stay coherent; an out-of-range value is rejected up front.
        var options = new ComputeOptions { ApexWindowFraction = fraction };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*ApexWindowFraction*");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void EnsureValid_throws_on_non_positive_brake_point_scale(float scale)
    {
        // M36: a zero/negative ms-per-metre scale would zero out (or invert) the brake-point channel in the
        // dominant-channel argmax, silently defeating the cross-unit ranking.
        var options = new ComputeOptions { MsPerMetreBrakePoint = scale };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MsPerMetreBrakePoint*");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void EnsureValid_throws_on_non_positive_throttle_resume_scale(float scale)
    {
        var options = new ComputeOptions { MsPerMetreThrottleResume = scale };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MsPerMetreThrottleResume*");
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void EnsureValid_throws_on_non_positive_min_speed_scale(float scale)
    {
        var options = new ComputeOptions { MsPerKmhMinSpeed = scale };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MsPerKmhMinSpeed*");
    }
}
