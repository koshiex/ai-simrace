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
    public void Ru_resolves_the_authored_spoken_form_ordinal_first()
    {
        var names = CornerNameMap.Load();

        names.GetRu("spa", "spa_t10").Should().Be("первый Пухон");
        names.GetRu("spa", "spa_t01").Should().Be("Ла-Сурс");
    }

    [Fact]
    public void Ru_falls_back_to_the_short_then_positional_form_when_unauthored()
    {
        var names = CornerNameMap.Load();

        // A ghost-derived track carries no authored names → GetRu falls through GetShort to the positional name.
        names.GetRu("silverstone", "silverstone_t07").Should().Be("поворот 7");
    }
}
