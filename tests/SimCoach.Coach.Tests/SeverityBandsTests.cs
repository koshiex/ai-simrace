using FluentAssertions;
using SimCoach.Coach.Actions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class SeverityBandsTests
{
    [Theory]
    [InlineData(CoachPhase.Brake, 10, CoachSeverity.High)]
    [InlineData(CoachPhase.Entry, 999, CoachSeverity.High)]
    [InlineData(CoachPhase.Apex, 1, CoachSeverity.Medium)]
    [InlineData(CoachPhase.Exit, 1, CoachSeverity.Low)]
    public void Projects_priority_to_default_band(CoachPhase phase, int rank, CoachSeverity expected)
    {
        var options = new CoachOptions();

        options.SeverityFor(new CoachPriority(phase, rank)).Should().Be(expected);
    }

    [Fact]
    public void EnsureValid_rejects_nonmonotonic_bands()
    {
        var options = new CoachOptions
        {
            SeverityBands =
            [
                new SeverityBand(new CoachPriority(CoachPhase.Apex, int.MaxValue), CoachSeverity.Medium),
                new SeverityBand(new CoachPriority(CoachPhase.Entry, int.MaxValue), CoachSeverity.High),
                new SeverityBand(new CoachPriority(CoachPhase.Exit, int.MaxValue), CoachSeverity.Low),
            ],
        };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*ascending*");
    }

    [Fact]
    public void EnsureValid_rejects_bands_not_covering_the_range()
    {
        var options = new CoachOptions
        {
            SeverityBands =
            [
                new SeverityBand(new CoachPriority(CoachPhase.Brake, int.MaxValue), CoachSeverity.High),
            ],
        };

        Action act = options.EnsureValid;

        act.Should().Throw<InvalidOperationException>().WithMessage("*cover*");
    }
}
