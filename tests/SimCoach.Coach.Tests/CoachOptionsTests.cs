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
    public void EnsureValid_throws_on_non_positive_catch_all_rank()
    {
        var options = new CoachOptions { CatchAllRank = 0 };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*CatchAllRank*");
    }

    [Fact]
    public void AllowsAbstain_only_for_a_corner_weak_catch_all_below_high()
    {
        var options = new CoachOptions();
        var catchAll = new SimCoach.Coach.Actions.CoachPriority(SimCoach.Coach.Actions.CoachPhase.Exit, 900);
        var specific = new SimCoach.Coach.Actions.CoachPriority(SimCoach.Coach.Actions.CoachPhase.Entry, 10);

        options.AllowsAbstain(CoachCadence.Corner, catchAll).Should().BeTrue();
        options.AllowsAbstain(CoachCadence.Sector, catchAll).Should().BeFalse(); // corner-only scope
        options.AllowsAbstain(CoachCadence.Lap, catchAll).Should().BeFalse();
        options.AllowsAbstain(CoachCadence.Corner, specific).Should().BeFalse(); // specific lead never abstains
    }

    [Fact]
    public void AllowsAbstain_never_for_a_high_severity_lead_even_at_catch_all_rank()
    {
        var options = new CoachOptions();
        // Entry-phase → High severity, but authored at the catch-all rank: the never-silent guard must win.
        var highRankCatchAll = new SimCoach.Coach.Actions.CoachPriority(SimCoach.Coach.Actions.CoachPhase.Entry, 900);

        options.SeverityFor(highRankCatchAll).Should().Be(SimCoach.Coach.Actions.CoachSeverity.High);
        options.AllowsAbstain(CoachCadence.Corner, highRankCatchAll).Should().BeFalse();
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
