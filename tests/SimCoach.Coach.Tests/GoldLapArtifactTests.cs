using FluentAssertions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldLapArtifactTests
{
    [Fact]
    public void Carries_absolute_time_bools_and_top_corner()
    {
        GoldArtifact<GoldLapEvent> art = GoldTestData.Builder().BuildLap(GoldTestData.Lap(), GoldTestData.Ctx());

        art.Cadence.Should().Be("lap");
        art.Event.LapNumber.Should().Be(7);
        art.Event.LapTimeMs.Should().Be(139450);
        art.Event.DeltaMs.Should().Be(210);
        art.Event.IsPb.Should().BeFalse();
        art.Event.IsClean.Should().BeTrue();
        art.Event.TopCorner.Should().Be("Rivage");
    }

    [Fact]
    public void Maps_top_losses_with_resolved_short_ru_names()
    {
        GoldLapEvent e = GoldTestData.Builder().BuildLap(GoldTestData.Lap(), GoldTestData.Ctx()).Event;

        e.TopLosses.Should().ContainSingle();
        e.TopLosses[0].Corner.Should().Be("Rivage");
        // spa_t08 → GetShort short RU form; guards against a regression to the empty default or raw ResolveName.
        e.TopLosses[0].CornerNameRu.Should().Be("Риваж");
    }

    [Fact]
    public void Rounds_thermal_temps_and_keeps_overheat_flags()
    {
        GoldThermalSummary thermal = GoldTestData.Builder().BuildLap(GoldTestData.Lap(), GoldTestData.Ctx()).Event.Thermal;

        thermal.MaxTyreTempC.Should().Be(98.6);
        thermal.MaxBrakeTempC.Should().Be(512.4);
        thermal.TyreOverheat.Should().BeTrue();
        thermal.BrakeOverheat.Should().BeFalse();
    }

    [Fact]
    public void Absent_thermal_message_becomes_a_zeroed_summary()
    {
        LapEvent lap = GoldTestData.Lap();
        lap.Thermal = null;

        GoldThermalSummary thermal = GoldTestData.Builder().BuildLap(lap, GoldTestData.Ctx()).Event.Thermal;

        thermal.MaxTyreTempC.Should().Be(0);
        thermal.MaxBrakeTempC.Should().Be(0);
        thermal.TyreOverheat.Should().BeFalse();
        thermal.BrakeOverheat.Should().BeFalse();
    }
}
