using FluentAssertions;
using SimCoach.Coach.Actions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class CoachPriorityTests
{
    [Fact]
    public void Brake_phase_outranks_exit_phase()
    {
        var brake = new CoachPriority(CoachPhase.Brake, 999);
        var exit = new CoachPriority(CoachPhase.Exit, 1);

        brake.Should().BeLessThan(exit);
        (brake < exit).Should().BeTrue();
    }

    [Fact]
    public void Lower_rank_is_more_urgent_within_a_phase()
    {
        var early = new CoachPriority(CoachPhase.Brake, 10);
        var late = new CoachPriority(CoachPhase.Brake, 20);

        early.Should().BeLessThan(late);
    }

    [Fact]
    public void Sorts_into_a_deterministic_total_order()
    {
        var unordered = new List<CoachPriority>
        {
            new(CoachPhase.Exit, 5),
            new(CoachPhase.Brake, 50),
            new(CoachPhase.Apex, 1),
            new(CoachPhase.Brake, 10),
            new(CoachPhase.Entry, 7),
        };

        List<CoachPriority> ordered = [.. unordered.OrderBy(p => p)];

        ordered.Should().ContainInConsecutiveOrder(
            new CoachPriority(CoachPhase.Brake, 10),
            new CoachPriority(CoachPhase.Brake, 50),
            new CoachPriority(CoachPhase.Entry, 7),
            new CoachPriority(CoachPhase.Apex, 1),
            new CoachPriority(CoachPhase.Exit, 5));
    }

    [Fact]
    public void Take_over_ordered_priorities_is_stable()
    {
        var priorities = new List<CoachPriority>
        {
            new(CoachPhase.Exit, 5),
            new(CoachPhase.Brake, 50),
            new(CoachPhase.Apex, 1),
            new(CoachPhase.Brake, 10),
            new(CoachPhase.Entry, 7),
            new(CoachPhase.Exit, 1),
        };

        List<CoachPriority> top3 = [.. priorities.OrderBy(p => p).Take(3)];

        top3.Should().Equal(
            new CoachPriority(CoachPhase.Brake, 10),
            new CoachPriority(CoachPhase.Brake, 50),
            new CoachPriority(CoachPhase.Entry, 7));
    }
}
