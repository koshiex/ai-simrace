using FluentAssertions;
using SimCoach.Reference;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class ReferenceKindTests
{
    [Theory]
    [InlineData(ReferenceKind.Pb, "pb")]
    [InlineData(ReferenceKind.Optimal, "optimal")]
    [InlineData(ReferenceKind.AlienLine, "alien_line")]
    public void ToDbString_maps_each_kind_to_its_stable_string(ReferenceKind kind, string expected) =>
        kind.ToDbString().Should().Be(expected);

    [Theory]
    [InlineData("pb", ReferenceKind.Pb)]
    [InlineData("optimal", ReferenceKind.Optimal)]
    [InlineData("alien_line", ReferenceKind.AlienLine)]
    public void Parse_maps_each_string_back_to_its_kind(string dbString, ReferenceKind expected) =>
        ReferenceKinds.Parse(dbString).Should().Be(expected);

    [Theory]
    [InlineData(ReferenceKind.Pb)]
    [InlineData(ReferenceKind.Optimal)]
    [InlineData(ReferenceKind.AlienLine)]
    public void ToDbString_and_Parse_round_trip(ReferenceKind kind) =>
        ReferenceKinds.Parse(kind.ToDbString()).Should().Be(kind);

    [Fact]
    public void Parse_throws_on_an_unknown_kind_string()
    {
        Action parse = () => ReferenceKinds.Parse("ghost");

        parse.Should().Throw<ArgumentException>().WithMessage("*ghost*");
    }

    [Fact]
    public void Parse_throws_on_null()
    {
        Action parse = () => ReferenceKinds.Parse(null!);

        parse.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToDbString_throws_on_an_undefined_enum_value()
    {
        Action map = () => ((ReferenceKind)999).ToDbString();

        map.Should().Throw<ArgumentOutOfRangeException>();
    }
}
