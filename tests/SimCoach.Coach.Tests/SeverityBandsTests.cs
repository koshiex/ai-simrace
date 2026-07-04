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

    [Theory]
    [InlineData(0.0, CoachSeverity.Low)]        // cold start / no reference / absent loss → Low, not never-silent
    [InlineData(99.9, CoachSeverity.Low)]
    [InlineData(100.0, CoachSeverity.Medium)]   // Medium floor is inclusive
    [InlineData(249.9, CoachSeverity.Medium)]
    [InlineData(250.0, CoachSeverity.High)]     // High floor is inclusive
    [InlineData(400.0, CoachSeverity.High)]
    [InlineData(-300.0, CoachSeverity.High)]    // signed loss (self−ref) → severity is the magnitude
    public void SeverityForLoss_projects_by_time_loss_magnitude(double lossMs, CoachSeverity expected)
    {
        var options = new CoachOptions();

        options.SeverityForLoss(lossMs).Should().Be(expected);
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
