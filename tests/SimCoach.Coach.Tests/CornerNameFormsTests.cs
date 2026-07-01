using FluentAssertions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class CornerNameFormsTests
{
    [Fact]
    public void Resolve_name_falls_back_to_positional_for_unknown_corner()
    {
        var names = CornerNameMap.Load();

        names.ResolveName("spa", "spa_t99").Should().Be("поворот 99");
    }

    [Fact]
    public void Resolve_name_is_positional_for_unknown_track()
    {
        var names = CornerNameMap.Load();

        names.ResolveName("silverstone", "silverstone_t07").Should().Be("поворот 7");
    }

    [Fact]
    public void Resolve_name_falls_back_instead_of_throwing_for_an_empty_corner_id()
    {
        var names = CornerNameMap.Load();

        Action act = () => names.ResolveName("spa", string.Empty);

        act.Should().NotThrow();
    }

    [Fact]
    public void Short_form_resolves_the_authored_value()
    {
        var names = CornerNameMap.Load();

        names.GetShort("spa", "spa_t02").Should().Be("О-Руж");
    }

    [Fact]
    public void Short_form_falls_back_to_full_name_when_unauthored()
    {
        var names = CornerNameMap.Load();

        names.GetShort("spa", "spa_t99").Should().Be("поворот 99");
    }

    [Fact]
    public void Spoken_strips_the_trailing_paren_and_expands_the_ordinal()
    {
        var names = CornerNameMap.Load();

        names.GetSpokenRu("spa", "spa_t03").Should().Be("Raidillon, первый");
    }

    [Fact]
    public void Spoken_returns_the_full_name_when_there_is_no_paren()
    {
        var names = CornerNameMap.Load();

        names.GetSpokenRu("spa", "spa_t02").Should().Be("Eau Rouge");
    }

    [Fact]
    public void Spoken_keeps_the_raw_paren_when_no_ordinal_is_authored()
    {
        CornerNameForms.Spoken("Foo (7)").Should().Be("Foo (7)");
    }
}
