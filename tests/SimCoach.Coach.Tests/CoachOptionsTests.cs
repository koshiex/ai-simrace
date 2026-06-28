using FluentAssertions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class CoachOptionsTests
{
    [Fact]
    public void EnsureValid_passes_for_defaults()
    {
        var options = new CoachOptions();

        Action act = options.EnsureValid;

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValid_throws_on_zero_word_budget()
    {
        var options = new CoachOptions { InCornerMaxWords = 0 };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*word budgets*");
    }

    [Fact]
    public void EnsureValid_throws_on_zero_menu_size()
    {
        var options = new CoachOptions { MaxActionsInMenu = 0 };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*MaxActionsInMenu*");
    }

    [Fact]
    public void EnsureValid_throws_when_a_cadence_route_key_is_missing()
    {
        var options = new CoachOptions
        {
            RouteKeys = new Dictionary<CoachCadence, string>
            {
                [CoachCadence.Corner] = "corner",
                [CoachCadence.Sector] = "sector",
                [CoachCadence.Lap] = "lap",
                [CoachCadence.Session] = "debrief",
                // Strategy intentionally omitted.
            },
        };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*Strategy*");
    }
}
