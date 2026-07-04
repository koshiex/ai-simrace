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

    [Fact]
    public void For_still_throws_for_strategy()
    {
        Action act = () => GoldFieldNames.For(CoachCadence.Strategy);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Session_set_is_the_scalar_surface_of_the_session_payload()
    {
        IReadOnlySet<string> session = GoldFieldNames.For(CoachCadence.Session);

        // Drift guard (M20): the exact flat scalar surface of GoldSessionPayload plus the header's has_reference,
        // excluding the non-scalar aggregates. Adding/removing a session scalar must update this pin in lockstep.
        session.Should().OnlyHaveUniqueItems();
        session.Should().BeEquivalentTo(new[]
        {
            "lap_count", "clean_lap_count", "pb_time_ms", "average_lap_ms", "understeer_trend",
            "consistency_stddev_ms", "theoretical_best_gap_ms", "has_reference",
        });
        session.Should().NotContain(["aggregated_losses", "sector_avg_delta_ms", "fuel_tyre", "stints", "top_losses"]);
    }

    [Theory]
    [InlineData(CoachCadence.Corner)]
    [InlineData(CoachCadence.Sector)]
    [InlineData(CoachCadence.Lap)]
    [InlineData(CoachCadence.Session)]
    public void Each_set_is_collision_free(CoachCadence cadence)
    {
        IReadOnlySet<string> set = GoldFieldNames.For(cadence);

        set.Should().OnlyHaveUniqueItems();
    }
}
