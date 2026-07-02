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
}
