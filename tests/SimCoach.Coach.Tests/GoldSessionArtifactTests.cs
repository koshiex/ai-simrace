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
        // M41: setup_hint is now synthesized from the per-phase balance grounds (entry-band understeer is the
        // dominant clearing band), no longer a hardcoded null.
        e.SetupHint.Should().Be("устойчивый снос на входе в поворот");
        e.Stints.Should().BeEmpty();
    }

    [Fact]
    public void Maps_balance_phase_trends_and_grounded_sector_membership()
    {
        GoldSessionPayload e = GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx()).Event;

        e.BalancePhaseTrends.Should().HaveCount(3);
        e.BalancePhaseTrends[0].Phase.Should().Be("entry");
        e.BalancePhaseTrends[0].Balance.Should().Be(0.32);
        e.BalancePhaseTrends[0].SampleCount.Should().Be(6);
        e.BalancePhaseTrends[2].Phase.Should().Be("exit");
        e.BalancePhaseTrends[2].Balance.Should().Be(-0.1);

        // corner_ids resolve to human names at the Coach layer (ADR-0010).
        e.SectorCornerMemberships.Should().HaveCount(2);
        e.SectorCornerMemberships[0].SectorIndex.Should().Be(0);
        e.SectorCornerMemberships[0].Corners.Should().Equal("La Source", "Eau Rouge");
    }

    [Fact]
    public void Carries_per_corner_loss_trend_ordered_by_lap()
    {
        GoldSessionPayload e = GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx()).Event;

        GoldAggregatedLoss eauRouge = e.AggregatedLosses[0];
        eauRouge.Corner.Should().Be("Eau Rouge");
        eauRouge.LossTrend.Should().HaveCount(3);
        eauRouge.LossTrend.Select(p => p.LapNumber).Should().Equal(1, 2, 3);
        eauRouge.LossTrend.Select(p => p.LossMs).Should().Equal(120, 90, 210);
        // The magnitude series is never the authoritative total.
        eauRouge.LossTrend.Sum(p => p.LossMs).Should().NotBe(eauRouge.TotalLossMs);
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
