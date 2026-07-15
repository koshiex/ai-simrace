using FluentAssertions;
using SimCoach.Coach;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class ChannelGlossTests
{
    [Theory]
    [InlineData("brake_point")]
    [InlineData("throttle_resume")]
    [InlineData("min_speed")]
    public void ToRu_glosses_each_signed_channel_code(string channel)
    {
        ChannelGloss.ToRu(channel).Should().Be(CoachStrings.Get("Channel_" + channel));
    }

    [Fact]
    public void ToRu_falls_back_to_the_neutral_gloss_for_an_empty_channel()
    {
        ChannelGloss.ToRu(string.Empty).Should().Be(CoachStrings.Get("Reason_slower"));
    }

    [Fact]
    public void ToRu_falls_back_to_the_neutral_gloss_for_an_unmapped_channel()
    {
        // A channel identifier with no Channel_* resx entry must resolve to the neutral localized gloss, never
        // the raw key ("Channel_line_deviation") which could reach voice/overlay.
        string ru = ChannelGloss.ToRu("line_deviation");

        ru.Should().Be(CoachStrings.Get("Reason_slower"));
        ru.Should().NotContain("Channel_");
    }
}
