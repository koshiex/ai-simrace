using FluentAssertions;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class DefaultCoachAmbientStateTests
{
    [Fact]
    public void Latest_gate_is_the_unknown_sentinel()
    {
        ICoachAmbientState ambient = new DefaultCoachAmbientState();
        ambient.LatestGate().Should().Be(GateSnapshot.Unknown);
        ambient.LatestGate().HasFrame.Should().BeFalse();
    }

    [Fact]
    public void Session_metadata_has_no_reference()
    {
        ICoachAmbientState ambient = new DefaultCoachAmbientState();
        GoldSessionContext metadata = ambient.SessionMetadata();
        metadata.HasReference.Should().BeFalse();
        metadata.Locale.Should().Be("ru-RU");
    }

    [Fact]
    public void Coach_service_options_defaults_to_llm_offline()
    {
        new CoachServiceOptions().LlmLive.Should().BeFalse();
    }
}
