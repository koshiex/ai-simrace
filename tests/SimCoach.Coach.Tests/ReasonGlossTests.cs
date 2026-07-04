using FluentAssertions;
using SimCoach.Coach;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class ReasonGlossTests
{
    [Fact]
    public void ToRu_glosses_a_mapped_reason_code()
    {
        ReasonGloss.ToRu("early_brake").Should().Be(CoachStrings.Get("Reason_early_brake"));
    }

    [Fact]
    public void ToRu_falls_back_to_the_neutral_gloss_for_an_empty_reason()
    {
        ReasonGloss.ToRu(string.Empty).Should().Be(CoachStrings.Get("Reason_slower"));
    }

    [Fact]
    public void ToRu_falls_back_to_the_neutral_gloss_for_an_unmapped_reason()
    {
        // An untranslated reason identifier has no Reason_* resx entry. It must resolve to the neutral
        // localized gloss, never the raw key ("Reason_totally_unknown_code") which could reach voice/overlay.
        string ru = ReasonGloss.ToRu("totally_unknown_code");

        ru.Should().Be(CoachStrings.Get("Reason_slower"));
        ru.Should().NotContain("Reason_");
    }
}
