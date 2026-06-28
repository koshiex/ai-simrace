using FluentAssertions;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldBoolAlwaysPresentTests
{
    [Fact]
    public void Corner_off_track_serializes_even_when_false_and_no_reference()
    {
        string json = GoldSerializer.Serialize(
            GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx(hasReference: false)));

        json.Should().Contain("\"off_track\":false");
    }

    [Fact]
    public void Lap_bools_serialize_even_when_false_and_thermal_absent()
    {
        LapEvent lap = GoldTestData.Lap();
        lap.IsClean = false;
        lap.Thermal = null;

        string json = GoldSerializer.Serialize(GoldTestData.Builder().BuildLap(lap, GoldTestData.Ctx(hasReference: false)));

        json.Should().Contain("\"is_pb\":false");
        json.Should().Contain("\"is_clean\":false");
        json.Should().Contain("\"tyre_overheat\":false");
        json.Should().Contain("\"brake_overheat\":false");
    }

    [Fact]
    public void Lap_overheat_bools_readable_through_the_view_when_false()
    {
        LapEvent lap = GoldTestData.Lap();
        lap.Thermal = null;

        IGoldView view = GoldView.For(GoldTestData.Builder().BuildLap(lap, GoldTestData.Ctx()));

        view.TryGetBool("tyre_overheat", out bool tyre).Should().BeTrue();
        tyre.Should().BeFalse();
        view.TryGetBool("brake_overheat", out bool brake).Should().BeTrue();
        brake.Should().BeFalse();
    }
}
