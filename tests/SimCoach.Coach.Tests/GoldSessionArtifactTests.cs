using FluentAssertions;
using SimCoach.Coach.Gold;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldSessionArtifactTests
{
    [Fact]
    public void Reads_metadata_off_the_event_and_class_off_the_context()
    {
        GoldArtifact<GoldSessionPayload> art = GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx());

        art.Cadence.Should().Be("session");
        art.Session.TrackId.Should().Be("spa");
        art.Session.Weather.Should().Be("dry-cool");
        art.Session.CarClass.Should().Be("gt3");
        art.Session.LapNumber.Should().BeNull();
    }

    [Fact]
    public void Carries_counts_consistency_theoretical_and_fuel_summary()
    {
        GoldSessionPayload e = GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx()).Event;

        e.LapCount.Should().Be(12);
        e.CleanLapCount.Should().Be(4);
        e.PbTimeMs.Should().Be(138500);
        e.AverageLapMs.Should().Be(139200);
        e.UndersteerTrend.Should().Be(0.14);
        e.ConsistencyStddevMs.Should().Be(230.4);
        e.TheoreticalBestGapMs.Should().Be(320);
        e.SectorAvgDeltaMs.Should().Equal(120, -30, 45);
        e.FuelTyre.AvgFuelPerLapL.Should().Be(2.83);
        e.FuelTyre.EndTyreWearPct.Should().Be(0);
        e.SetupHint.Should().BeNull();
        e.Stints.Should().BeEmpty();
    }

    [Fact]
    public void Resolves_aggregated_loss_names()
    {
        GoldSessionPayload e = GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx()).Event;

        e.AggregatedLosses[0].Corner.Should().Be("Eau Rouge");
        e.AggregatedLosses[0].CornerNameRu.Should().Be("О-Руж");
        e.AggregatedLosses[0].TotalLossMs.Should().Be(600);
        // dominant_reason (field 5) is retained for back-compat; the M36 dominant_channel/value ride alongside.
        e.AggregatedLosses[0].Reason.Should().Be("low_min_speed");
        e.AggregatedLosses[0].DominantChannel.Should().Be("min_speed");
        e.AggregatedLosses[0].DominantChannelValue.Should().Be(48);
    }

    [Fact]
    public void Resolves_aggregated_loss_names_from_the_event_track_not_the_context()
    {
        // Context track deliberately disagrees with the SessionEvent track; names must follow the event.
        GoldSessionPayload e = GoldTestData.Builder()
            .BuildSession(GoldTestData.Session(), GoldTestData.Ctx(track: "silverstone"))
            .Event;

        e.AggregatedLosses[0].Corner.Should().Be("Eau Rouge");
    }
}
