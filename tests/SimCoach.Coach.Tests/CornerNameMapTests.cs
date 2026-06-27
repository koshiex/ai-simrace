using FluentAssertions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class CornerNameMapTests
{
    [Fact]
    public void Resolves_authored_monza_names()
    {
        var names = CornerNameMap.Load();

        names.TryGetName("monza", "monza_t03", out string curvaGrande).Should().BeTrue();
        curvaGrande.Should().Be("Curva Grande");

        names.TryGetName("monza", "monza_t11", out string parabolica).Should().BeTrue();
        parabolica.Should().Be("Curva Parabolica");
    }

    [Fact]
    public void Returns_false_for_unknown_track_or_corner()
    {
        var names = CornerNameMap.Load();

        names.TryGetName("monza", "monza_t99", out _).Should().BeFalse();
        names.TryGetName("silverstone", "silverstone_t01", out _).Should().BeFalse();
    }
}
