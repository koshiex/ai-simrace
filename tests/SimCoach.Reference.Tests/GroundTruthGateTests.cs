using FluentAssertions;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class GroundTruthGateTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("required")]
    public void IsRequired_true_for_a_set_non_disabling_flag(string flag)
        => GroundTruthGate.IsRequired(flag).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("False")]
    public void IsRequired_false_when_unset_or_explicitly_disabled(string? flag)
        => GroundTruthGate.IsRequired(flag).Should().BeFalse();
}
