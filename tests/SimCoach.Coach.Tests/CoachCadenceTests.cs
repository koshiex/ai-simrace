using FluentAssertions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class CoachCadenceTests
{
    [Fact]
    public void Has_five_values_including_reserved_strategy()
    {
        Enum.GetValues<CoachCadence>().Should().BeEquivalentTo(new[]
        {
            CoachCadence.Corner,
            CoachCadence.Sector,
            CoachCadence.Lap,
            CoachCadence.Session,
            CoachCadence.Strategy,
        });

        Enum.IsDefined(CoachCadence.Strategy).Should().BeTrue();
    }
}
