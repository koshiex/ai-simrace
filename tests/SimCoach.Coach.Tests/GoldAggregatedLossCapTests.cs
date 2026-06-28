using FluentAssertions;
using SimCoach.Coach.Gold;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldAggregatedLossCapTests
{
    [Fact]
    public void Caps_to_max_debrief_losses_ordered_by_total_loss_desc()
    {
        GoldSessionPayload e = GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx()).Event;

        // Fixture supplies six losses; the default cap is five.
        e.AggregatedLosses.Should().HaveCount(5);
        e.AggregatedLosses.Select(l => l.TotalLossMs).Should().BeInDescendingOrder();
        e.AggregatedLosses.Select(l => l.TotalLossMs).Should().Equal(600, 450, 300, 250, 200);
    }

    [Fact]
    public void Resolves_known_and_unknown_corner_ids()
    {
        GoldSessionPayload e = GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx()).Event;

        e.AggregatedLosses[0].Corner.Should().Be("Eau Rouge");
        e.AggregatedLosses.Select(l => l.Corner).Should().Contain("поворот 99");
    }
}
