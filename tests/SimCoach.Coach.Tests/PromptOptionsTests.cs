using FluentAssertions;
using SimCoach.Coach;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class PromptOptionsTests
{
    [Fact]
    public void Defaults_pass_validation()
    {
        Action act = () => new PromptOptions().EnsureValid();

        act.Should().NotThrow();
    }

    [Fact]
    public void Throws_on_an_empty_system_version()
    {
        var options = new PromptOptions
        {
            Cadences = new Dictionary<CoachCadence, PromptSelection>
            {
                [CoachCadence.Corner] = new PromptSelection(SystemVersion: " "),
                [CoachCadence.Sector] = new PromptSelection(),
                [CoachCadence.Lap] = new PromptSelection(),
                [CoachCadence.Session] = new PromptSelection(),
            },
        };

        Action act = () => options.EnsureValid();

        act.Should().Throw<InvalidOperationException>().WithMessage("*PromptOptions*SystemVersion*");
    }

    [Fact]
    public void Throws_when_a_real_cadence_is_missing()
    {
        var options = new PromptOptions
        {
            Cadences = new Dictionary<CoachCadence, PromptSelection>
            {
                [CoachCadence.Corner] = new PromptSelection(),
                [CoachCadence.Sector] = new PromptSelection(),
                [CoachCadence.Lap] = new PromptSelection(),
            },
        };

        Action act = () => options.EnsureValid();

        act.Should().Throw<InvalidOperationException>().WithMessage("*PromptOptions*Session*");
    }
}
