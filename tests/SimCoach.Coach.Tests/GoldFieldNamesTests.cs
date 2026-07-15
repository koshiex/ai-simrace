using System.Reflection;
using System.Text;
using FluentAssertions;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldFieldNamesTests
{
    [Fact]
    public void Corner_set_contains_b1_and_derived_fields()
    {
        IReadOnlySet<string> corner = GoldFieldNames.For(CoachCadence.Corner);

        corner.Should().Contain(["wheelspin_score", "brake_lockup_score", "brake_overlap_steer_pct", "steering_jitter", "trail_brake_diff_pct"]);
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
    public void Session_set_is_the_reflected_scalar_surface_of_the_session_payload()
    {
        IReadOnlySet<string> session = GoldFieldNames.For(CoachCadence.Session);

        // Real drift guard (M20): derive the expectation by reflecting GoldSessionPayload's scalar properties,
        // applying the documented exclusions (SetupHint has no MVP source), snake-casing, and adding the
        // header's has_reference — so adding a new payload scalar actually fails this test until the catalog is
        // updated in lockstep, rather than the pin silently tracking whatever the code already produces.
        string[] expected =
        [
            .. typeof(GoldSessionPayload)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => IsScalar(p.PropertyType) && p.Name != nameof(GoldSessionPayload.SetupHint))
                .Select(p => ToSnakeCase(p.Name)),
            "has_reference",
        ];

        session.Should().BeEquivalentTo(expected);
        session.Should().OnlyHaveUniqueItems();
        session.Should().NotContain(["aggregated_losses", "sector_avg_delta_ms", "fuel_tyre", "stints", "top_losses"]);
    }

    private static bool IsScalar(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying == typeof(string) || underlying == typeof(decimal);
    }

    private static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
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
