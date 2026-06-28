using System.Globalization;
using FluentAssertions;
using SimCoach.Coach.Gold;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldDeterminismTests
{
    [Fact]
    public void Same_input_serializes_byte_identically()
    {
        GoldArtifactBuilder builder = GoldTestData.Builder();

        string first = GoldSerializer.Serialize(builder.BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx()));
        string second = GoldSerializer.Serialize(builder.BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx()));

        first.Should().Be(second);
    }

    [Fact]
    public void Corner_floats_serialize_in_short_decimal_form()
    {
        string json = GoldSerializer.Serialize(GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx()));

        json.Should().Contain("\"trail_brake_pct_self\":0.22");
        json.Should().Contain("\"wheelspin_score\":0.18");
        json.Should().NotContain("0.2199");
    }

    [Fact]
    public void Lap_and_session_floats_serialize_in_short_decimal_form()
    {
        GoldArtifactBuilder builder = GoldTestData.Builder();

        string lap = GoldSerializer.Serialize(builder.BuildLap(GoldTestData.Lap(), GoldTestData.Ctx()));
        string session = GoldSerializer.Serialize(builder.BuildSession(GoldTestData.Session(), GoldTestData.Ctx()));

        lap.Should().Contain("\"max_tyre_temp_c\":98.6");
        lap.Should().NotContain("98.63");
        session.Should().Contain("\"understeer_trend\":0.14");
        session.Should().Contain("\"avg_fuel_per_lap_l\":2.83");
        session.Should().NotContain("2.8339");
    }

    [Fact]
    public void Serialization_is_culture_invariant()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            string json = GoldSerializer.Serialize(GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx()));

            json.Should().Contain("\"trail_brake_pct_self\":0.22");
            json.Should().NotContain("0,22");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
