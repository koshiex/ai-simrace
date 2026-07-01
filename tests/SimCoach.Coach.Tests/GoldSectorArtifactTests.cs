using FluentAssertions;
using SimCoach.Coach.Gold;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldSectorArtifactTests
{
    [Fact]
    public void Carries_absolute_time_delta_and_top_corner()
    {
        GoldArtifact<GoldSectorEvent> art = GoldTestData.Builder().BuildSector(GoldTestData.Sector(), GoldTestData.Ctx());

        art.Cadence.Should().Be("sector");
        art.Event.SectorIdx.Should().Be(1);
        art.Event.SectorTimeMs.Should().Be(41230);
        art.Event.DeltaMs.Should().Be(180);
        art.Event.TopCorner.Should().Be("Les Combes (1)");
    }

    [Fact]
    public void Maps_top_losses_with_resolved_names()
    {
        GoldSectorEvent e = GoldTestData.Builder().BuildSector(GoldTestData.Sector(), GoldTestData.Ctx()).Event;

        e.TopLosses.Should().HaveCount(2);
        e.TopLosses[0].Corner.Should().Be("Les Combes (1)");
        e.TopLosses[0].Ms.Should().Be(120);
        e.TopLosses[0].Why.Should().Be("late_throttle");
        // spa_t05 → GetShort short RU form; guards against a regression to the empty default or raw ResolveName.
        e.TopLosses[0].CornerNameRu.Should().Be("Комб1");
        e.TopLosses[1].Corner.Should().Be("Eau Rouge");
    }
}
