using FluentAssertions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldHasReferenceDropTests
{
    private static string CornerJson(bool hasReference) =>
        GoldSerializer.Serialize(GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx(hasReference)));

    private static string SessionJson(SessionEvent ev, bool hasReference) =>
        GoldSerializer.Serialize(GoldTestData.Builder().BuildSession(ev, GoldTestData.Ctx(hasReference)));

    [Fact]
    public void Corner_drops_reference_relative_fields_without_a_reference()
    {
        string json = CornerJson(hasReference: false);

        json.Should().NotContain("delta_ms");
        json.Should().NotContain("brake_point_diff_m");
        json.Should().NotContain("min_speed_diff_kmh");
        json.Should().NotContain("throttle_resume_diff_m");
        json.Should().NotContain("racing_line_deviation_m");
        json.Should().NotContain("entry_line_deviation_m");
        json.Should().NotContain("apex_line_deviation_m");
        json.Should().NotContain("exit_line_deviation_m");
        json.Should().NotContain("brake_release_diff_m");
        json.Should().NotContain("trail_brake_pct_ref");
        json.Should().NotContain("trail_brake_diff_pct");
    }

    [Fact]
    public void Corner_keeps_self_only_fields_without_a_reference()
    {
        string json = CornerJson(hasReference: false);

        json.Should().Contain("trail_brake_pct_self");
        json.Should().Contain("understeer_score");
        json.Should().Contain("wheelspin_score");
        json.Should().Contain("brake_lockup_score");
        json.Should().Contain("short_shift_score");
        json.Should().Contain("off_track");
        json.Should().Contain("corner_name");
    }

    [Fact]
    public void Sector_drops_delta_without_a_reference()
    {
        string json = GoldSerializer.Serialize(
            GoldTestData.Builder().BuildSector(GoldTestData.Sector(), GoldTestData.Ctx(hasReference: false)));

        json.Should().NotContain("delta_ms");
        json.Should().Contain("sector_time_ms");
    }

    [Fact]
    public void Lap_drops_delta_without_a_reference()
    {
        string json = GoldSerializer.Serialize(
            GoldTestData.Builder().BuildLap(GoldTestData.Lap(), GoldTestData.Ctx(hasReference: false)));

        json.Should().NotContain("delta_ms");
        json.Should().Contain("lap_time_ms");
    }

    [Fact]
    public void Session_drops_sector_avg_delta_without_a_reference_but_keeps_consistency()
    {
        string json = SessionJson(GoldTestData.Session(), hasReference: false);

        json.Should().NotContain("sector_avg_delta_ms");
        json.Should().Contain("consistency_stddev_ms");
        json.Should().Contain("theoretical_best_gap_ms");
    }

    [Fact]
    public void Session_drops_consistency_with_fewer_than_two_clean_laps()
    {
        SessionEvent ev = GoldTestData.Session();
        ev.CleanLapCount = 1;

        string json = SessionJson(ev, hasReference: true);

        json.Should().NotContain("consistency_stddev_ms");
        json.Should().Contain("theoretical_best_gap_ms");
    }

    [Fact]
    public void Session_drops_theoretical_best_with_no_clean_lap()
    {
        SessionEvent ev = GoldTestData.Session();
        ev.CleanLapCount = 0;

        string json = SessionJson(ev, hasReference: true);

        json.Should().NotContain("theoretical_best_gap_ms");
        json.Should().NotContain("consistency_stddev_ms");
    }

    [Fact]
    public void Session_drops_pb_and_average_when_not_yet_known()
    {
        SessionEvent ev = GoldTestData.Session();
        ev.PbTimeMs = 0;
        ev.AverageLapMs = 0;

        string json = SessionJson(ev, hasReference: true);

        json.Should().NotContain("pb_time_ms");
        json.Should().NotContain("average_lap_ms");
    }
}
