using FluentAssertions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldCornerArtifactTests
{
    [Fact]
    public void Builds_envelope_and_session_header()
    {
        GoldArtifact<GoldCornerEvent> art = GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx());

        art.SchemaVersion.Should().Be("gold/1");
        art.Cadence.Should().Be("corner");
        art.Locale.Should().Be("ru-RU");
        art.Session.TrackId.Should().Be("spa");
        art.Session.CarClass.Should().Be("gt3");
        art.Session.Weather.Should().Be("dry-cool");
        art.Session.LapNumber.Should().Be(7);
        art.Session.HasReference.Should().BeTrue();
    }

    [Fact]
    public void Resolves_corner_name_and_b1_scores()
    {
        GoldCornerEvent e = GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx()).Event;

        e.CornerName.Should().Be("Eau Rouge");
        e.CornerNameRu.Should().Be("О-Руж");
        e.WheelspinScore.Should().Be(0.18);
        e.BrakeLockupScore.Should().Be(0.55);
        e.ShortShiftScore.Should().Be(0.42);
        e.BrakeOverlapSteerPct.Should().Be(0.31);
        e.SteeringJitter.Should().Be(0.09);
        e.Reason.Should().Be("low_min_speed");
    }

    [Fact]
    public void Brake_lockup_score_is_present_even_without_a_reference()
    {
        // Self-derived → non-nullable and never gated on a reference (unlike the *_diff_m fields).
        GoldCornerEvent e = GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx(hasReference: false)).Event;

        e.BrakeLockupScore.Should().Be(0.55);
        e.ShortShiftScore.Should().Be(0.42);
    }

    [Fact]
    public void Rounds_distances_and_speeds_and_derives_trail_brake_diff()
    {
        GoldCornerEvent e = GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx()).Event;

        e.BrakePointDiffM.Should().Be(-3.4);
        e.MinSpeedDiffKmh.Should().Be(-5.1);
        e.ThrottleResumeDiffM.Should().Be(-2.8);
        e.RacingLineDeviationM.Should().Be(0.7);
        e.TrailBrakePctSelf.Should().Be(0.22);
        e.TrailBrakePctRef.Should().Be(0.41);
        e.TrailBrakeDiffPct.Should().Be(-0.19);
    }

    [Fact]
    public void Surfaces_signed_per_phase_line_deviation_rounded()
    {
        GoldCornerEvent e = GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx()).Event;

        e.EntryLineDeviationM.Should().Be(0.6, "+ = wider than the reference line on entry");
        e.ApexLineDeviationM.Should().Be(-0.4, "- = tighter than the reference line at the apex");
        e.ExitLineDeviationM.Should().Be(1.2);
    }

    [Fact]
    public void Empty_reason_becomes_null()
    {
        CornerEvent ev = GoldTestData.Corner();
        ev.Reason = string.Empty;

        GoldCornerEvent e = GoldTestData.Builder().BuildCorner(ev, GoldTestData.Ctx()).Event;

        e.Reason.Should().BeNull();
    }
}
