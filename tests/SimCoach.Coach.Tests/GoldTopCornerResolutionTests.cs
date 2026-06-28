using FluentAssertions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldTopCornerResolutionTests
{
    private static string? TopCorner(SectorEvent ev, string track = "spa") =>
        GoldTestData.Builder().BuildSector(ev, GoldTestData.Ctx(track: track)).Event.TopCorner;

    [Fact]
    public void Resolves_the_authored_name_of_the_biggest_loss()
    {
        TopCorner(GoldTestData.Sector()).Should().Be("Les Combes (1)");
    }

    [Fact]
    public void Empty_top_losses_yields_a_null_top_corner()
    {
        SectorEvent ev = GoldTestData.Sector();
        ev.TopLosses.Clear();

        TopCorner(ev).Should().BeNull();
    }

    [Fact]
    public void Unknown_corner_falls_back_to_positional()
    {
        SectorEvent ev = GoldTestData.Sector();
        ev.TopLosses.Clear();
        ev.TopLosses.Add(new CornerLoss { CornerId = "spa_t99", DeltaMs = 100, Reason = "x" });

        TopCorner(ev).Should().Be("поворот 99");
    }

    [Fact]
    public void Unknown_track_makes_all_names_positional()
    {
        TopCorner(GoldTestData.Sector(), track: "silverstone").Should().Be("поворот 5");
    }
}
