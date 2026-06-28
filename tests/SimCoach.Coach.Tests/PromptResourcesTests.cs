using System.Reflection;
using FluentAssertions;
using SimCoach.Coach;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class PromptResourcesTests
{
    [Fact]
    public void All_default_resources_resolve()
    {
        Action act = () => PromptResources.AssertAllResolve(new PromptOptions());

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("SimCoach.Coach.Prompts.coach.system.v1.ru.txt")]
    [InlineData("SimCoach.Coach.Prompts.coach.system.debrief.v1.ru.txt")]
    [InlineData("SimCoach.Coach.Prompts.coach.fewshot.v1.ru.json")]
    public void Manifest_carries_the_pinned_resource_name(string expected)
    {
        Assembly assembly = typeof(PromptBuilder).Assembly;

        assembly.GetManifestResourceNames().Should().Contain(expected);
    }

    [Fact]
    public void Throws_when_a_referenced_version_is_missing()
    {
        var options = new PromptOptions
        {
            Cadences = new Dictionary<CoachCadence, PromptSelection>
            {
                [CoachCadence.Corner] = new PromptSelection(SystemVersion: "v999"),
                [CoachCadence.Sector] = new PromptSelection(),
                [CoachCadence.Lap] = new PromptSelection(),
                [CoachCadence.Session] = new PromptSelection(),
            },
        };

        Action act = () => PromptResources.AssertAllResolve(options);

        act.Should().Throw<InvalidOperationException>().WithMessage("*was not found*");
    }
}
