using FluentAssertions;
using SimCoach.Coach.Rules;
using SimCoach.Pipeline.Kernels;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Coach.Tests.Rules;

public sealed class CornerPhaseResolverTests
{
    private readonly CornerPhaseResolver _resolver = new(new RuleEngineOptions { ApexWindowFraction = 0.25 });

    private static readonly IReadOnlyList<Corner> _oneCorner =
        [new Corner { Id = "t1", StartPosition = 0.10f, ApexPosition = 0.15f, EndPosition = 0.25f }];

    // start 0.10, apex 0.15, end 0.25, fraction 0.25 → apex band offset [0.0375, 0.075].
    [Theory]
    [InlineData(0.11, GateCornerPhase.Braking)] // just inside the window — braking zone
    [InlineData(0.13, GateCornerPhase.Entry)]   // turn-in
    [InlineData(0.15, GateCornerPhase.Apex)]    // apex
    [InlineData(0.20, GateCornerPhase.Exit)]    // past apex
    public void Resolves_phase_within_a_corner_window(double position, GateCornerPhase expected)
    {
        _resolver.Resolve(position, _oneCorner).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.05)] // before the window
    [InlineData(0.50)] // long after the window
    public void Position_outside_every_window_is_none(double position)
    {
        _resolver.Resolve(position, _oneCorner).Should().Be(GateCornerPhase.None);
    }

    [Theory]
    [InlineData(0.98, GateCornerPhase.Apex)]
    [InlineData(0.00, GateCornerPhase.Exit)]
    [InlineData(0.96, GateCornerPhase.Braking)]
    public void Handles_corners_that_wrap_the_start_finish_line(double position, GateCornerPhase expected)
    {
        IReadOnlyList<Corner> wrapping =
            [new Corner { Id = "t_last", StartPosition = 0.95f, ApexPosition = 0.98f, EndPosition = 0.05f }];

        _resolver.Resolve(position, wrapping).Should().Be(expected);
    }

    [Fact]
    public void No_baked_geometry_resolves_to_none()
    {
        _resolver.Resolve(0.5, []).Should().Be(GateCornerPhase.None);
    }

    // M9 parity: the resolver and the brake-overlap metric share ONE apex-band definition
    // (CornerPhaseBands). The phase boundaries the resolver classifies against are exactly the shared
    // helper's offsets, so the live gate and the metric can never disagree about "apex".
    [Fact]
    public void Phase_boundaries_come_from_the_shared_corner_phase_helper()
    {
        Corner corner = _oneCorner[0];
        CornerPhaseOffsets offsets = CornerPhaseBands.Offsets(
            corner.StartPosition, corner.ApexPosition, corner.EndPosition, 0.25);

        // A position just inside the apex band resolves to Apex; a hair before turn-in start is Braking;
        // between turn-in start and apex start is Entry — all keyed off the shared offsets.
        _resolver.Resolve(corner.StartPosition + offsets.ApexStart + 1e-4, _oneCorner).Should().Be(GateCornerPhase.Apex);
        _resolver.Resolve(corner.StartPosition + offsets.TurnInStart - 1e-4, _oneCorner).Should().Be(GateCornerPhase.Braking);
        _resolver.Resolve(corner.StartPosition + offsets.TurnInStart + 1e-4, _oneCorner).Should().Be(GateCornerPhase.Entry);
    }
}
