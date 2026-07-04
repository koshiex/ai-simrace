using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using Xunit;

namespace SimCoach.Pipeline.Tests.Segmentation;

public sealed class CleanLapPredicateTests
{
    private const int BlackFlagBit = 1 << 2;
    private const int PenaltyBit = 1 << 5;

    [Fact]
    public void Fully_valid_non_pit_lap_is_clean()
    {
        IReadOnlyList<TelemetryFrame> frames = [ValidFrame(), ValidFrame(), ValidFrame()];

        CleanLapPredicate.IsClean(frames).Should().BeTrue();
    }

    [Fact]
    public void Single_pit_lane_frame_disqualifies_the_lap()
    {
        TelemetryFrame pit = ValidFrame();
        pit.IsInPitLane = true;
        IReadOnlyList<TelemetryFrame> frames = [ValidFrame(), pit, ValidFrame()];

        CleanLapPredicate.IsClean(frames).Should().BeFalse();
    }

    [Fact]
    public void Invalid_lap_frame_disqualifies_the_lap()
    {
        TelemetryFrame invalid = ValidFrame();
        invalid.IsValidLap = false;
        IReadOnlyList<TelemetryFrame> frames = [ValidFrame(), invalid];

        CleanLapPredicate.IsClean(frames).Should().BeFalse();
    }

    [Fact]
    public void Tyres_out_frame_disqualifies_the_lap()
    {
        TelemetryFrame off = ValidFrame();
        off.TyresOut = 2;
        IReadOnlyList<TelemetryFrame> frames = [ValidFrame(), off];

        CleanLapPredicate.IsClean(frames).Should().BeFalse();
    }

    [Theory]
    [InlineData(BlackFlagBit)]
    [InlineData(PenaltyBit)]
    public void Disqualifying_flag_frame_disqualifies_the_lap(int flag)
    {
        TelemetryFrame flagged = ValidFrame();
        flagged.FlagsActive = flag;
        IReadOnlyList<TelemetryFrame> frames = [ValidFrame(), flagged];

        CleanLapPredicate.IsClean(frames).Should().BeFalse();
    }

    [Fact]
    public void Empty_lap_is_not_clean()
    {
        CleanLapPredicate.IsClean([]).Should().BeFalse();
    }

    private static TelemetryFrame ValidFrame() => new()
    {
        IsValidLap = true,
        IsInPitLane = false,
        TyresOut = 0,
        FlagsActive = 0,
    };
}
