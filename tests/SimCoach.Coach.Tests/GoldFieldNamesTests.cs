using FluentAssertions;
using SimCoach.Coach.Actions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldFieldNamesTests
{
    [Fact]
    public void Corner_set_contains_b1_and_derived_fields()
    {
        IReadOnlySet<string> corner = GoldFieldNames.For(CoachCadence.Corner);

        corner.Should().Contain(["wheelspin_score", "brake_overlap_steer_pct", "steering_jitter", "trail_brake_diff_pct"]);
    }

    [Fact]
    public void Corner_set_excludes_non_clause_fields()
    {
        IReadOnlySet<string> corner = GoldFieldNames.For(CoachCadence.Corner);

        corner.Should().NotContain("sector_idx");
        corner.Should().NotContain("top_losses");
    }

    [Fact]
    public void Lap_set_contains_thermal_fields()
    {
        IReadOnlySet<string> lap = GoldFieldNames.For(CoachCadence.Lap);

        lap.Should().Contain(["max_tyre_temp_c", "max_brake_temp_c", "tyre_overheat", "brake_overheat"]);
    }

    [Fact]
    public void Sector_set_excludes_corner_only_fields()
    {
        IReadOnlySet<string> sector = GoldFieldNames.For(CoachCadence.Sector);

        sector.Should().NotContain("brake_point_diff_m");
        sector.Should().NotContain("top_losses");
    }

    [Theory]
    [InlineData(CoachCadence.Session)]
    [InlineData(CoachCadence.Strategy)]
    public void For_throws_for_cadences_without_a_set(CoachCadence cadence)
    {
        Action act = () => GoldFieldNames.For(cadence);

        act.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(CoachCadence.Corner)]
    [InlineData(CoachCadence.Sector)]
    [InlineData(CoachCadence.Lap)]
    public void Each_set_is_collision_free(CoachCadence cadence)
    {
        IReadOnlySet<string> set = GoldFieldNames.For(cadence);

        set.Should().OnlyHaveUniqueItems();
    }
}
