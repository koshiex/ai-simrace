using FluentAssertions;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// M3 defence-in-depth: the plausibility helper is pure and independent of the M1 latch / M2 span
/// alignment, so these assertions hold even if both regress. Tier A catches the Curva Grande gain
/// rendered as a loss; Tier B catches the +14799 ms S1 loss on a lap that actually gained.
/// </summary>
public sealed class LossPlausibilityTests
{
    private const int CornerCeilingMs = 2000;
    private const float Ratio = 1.0f;
    private const int FloorMs = 300;

    [Theory]
    [InlineData(-3929)] // gain rendered as a loss by corner_catch_all's abs_round0
    [InlineData(3929)]  // oversized loss
    public void Tier_a_rejects_magnitude_over_ceiling_regardless_of_sign(int deltaMs)
    {
        LossPlausibility.WithinCeiling(deltaMs, CornerCeilingMs)
            .Should().BeFalse("|delta| exceeds the corner ceiling, so it is implausible either way");
    }

    [Theory]
    [InlineData(150)]
    [InlineData(-2000)]
    [InlineData(2000)] // exactly at the ceiling is admitted (<=)
    public void Tier_a_admits_magnitude_within_ceiling(int deltaMs)
    {
        LossPlausibility.WithinCeiling(deltaMs, CornerCeilingMs).Should().BeTrue();
    }

    [Fact]
    public void Tier_b_drops_the_positive_s1_loss_against_a_negative_lap_deficit()
    {
        // The headline debrief lie: S1 reported +14799 ms lost on a lap that gained 1381 ms overall.
        // The budget is max(1.0 * 1381, 300) = 1381 ms, which 14799 dwarfs.
        LossPlausibility.WithinDeficit(14799, lapDeficitMs: -1381, Ratio, FloorMs)
            .Should().BeFalse("14799 dwarfs the 1381 ms deficit budget");
    }

    [Fact]
    public void Tier_b_compares_against_the_lap_deficit_never_the_sector_absolute()
    {
        // The 14799 < 35994 trap: a naive guard comparing against the sector absolute time would ADMIT
        // 14799 (it is smaller). Keyed on the lap deficit instead, 14799 is dropped. This asserts the
        // comparand is the deficit, not the sector absolute.
        const int sectorAbsoluteMs = 35994;
        LossPlausibility.WithinDeficit(14799, lapDeficitMs: -1381, Ratio, FloorMs)
            .Should().BeFalse();
        (14799 < sectorAbsoluteMs).Should().BeTrue("proving the trap: 14799 is below the sector absolute");
    }

    [Fact]
    public void Tier_b_is_inert_on_a_loss_within_the_deficit_budget()
    {
        // A lap that genuinely lost 2000 ms may plausibly lose most of it in one sector.
        LossPlausibility.WithinDeficit(1500, lapDeficitMs: 2000, Ratio, FloorMs)
            .Should().BeTrue("1500 fits the 2000 ms budget");
    }

    [Fact]
    public void Tier_b_uses_the_floor_for_a_near_zero_deficit()
    {
        // With a ~0 lap deficit the ratio term collapses; the floor keeps a genuinely small loss valid
        // and there is no divide-by-zero (the budget is a max, not a ratio).
        LossPlausibility.WithinDeficit(250, lapDeficitMs: 0, Ratio, FloorMs)
            .Should().BeTrue("250 fits the 300 ms floor when the deficit is ~0");
        LossPlausibility.WithinDeficit(400, lapDeficitMs: 0, Ratio, FloorMs)
            .Should().BeFalse("400 exceeds the 300 ms floor with no deficit budget to draw on");
    }

    [Fact]
    public void Tier_b_scales_the_budget_by_the_ratio()
    {
        // ratio 1.2 widens the budget: 1.2 * 1000 = 1200, so an 1100 ms loss is admitted where ratio 1.0
        // would reject it.
        LossPlausibility.WithinDeficit(1100, lapDeficitMs: 1000, ratio: 1.0f, FloorMs)
            .Should().BeFalse();
        LossPlausibility.WithinDeficit(1100, lapDeficitMs: 1000, ratio: 1.2f, FloorMs)
            .Should().BeTrue();
    }
}
