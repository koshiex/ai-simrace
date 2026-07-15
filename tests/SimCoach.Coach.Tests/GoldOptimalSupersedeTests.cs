using FluentAssertions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Coach.Tests;

/// <summary>
/// M46 (must-fix #4): the cross-session own-optimal gap SUPERSEDES the within-session theoretical best. When a
/// persisted optimal fed the session (a non-empty sector_optimal_gap vector) the Gold layer surfaces
/// <c>optimal_gap_ms</c> and drops field-16; on the first-ever session it falls back to field-16 and omits the
/// optimal fields — one gap number for the LLM either way.
/// </summary>
public sealed class GoldOptimalSupersedeTests
{
    private static string SessionJson(SessionEvent ev) =>
        GoldSerializer.Serialize(GoldTestData.Builder().BuildSession(ev, GoldTestData.Ctx(hasReference: true)));

    private static SessionEvent WithOptimal(int gap, params int[] sectorDeficits)
    {
        SessionEvent ev = GoldTestData.Session();
        ev.OptimalGapMs = gap;
        ev.SectorOptimalGapMs.AddRange(sectorDeficits);
        return ev;
    }

    [Fact]
    public void Optimal_gap_supersedes_field_16_when_a_cross_session_optimal_exists()
    {
        string json = SessionJson(WithOptimal(1044, 120, 40, 884));

        json.Should().Contain("\"optimal_gap_ms\":1044");
        json.Should().Contain("\"sector_optimal_gap_ms\":");
        json.Should().NotContain(
            "theoretical_best_gap_ms", "the within-session number demotes when a cross-session optimal exists");
    }

    [Fact]
    public void Field_16_is_the_first_session_fallback_when_no_optimal_exists()
    {
        // The default fixture carries an empty sector_optimal vector → no persisted optimal for the triple.
        string json = SessionJson(GoldTestData.Session());

        json.Should().Contain("theoretical_best_gap_ms");
        json.Should().NotContain("\"optimal_gap_ms\":");
        json.Should().NotContain("sector_optimal_gap_ms");
    }

    [Fact]
    public void Payload_carries_the_gap_and_positional_deficit_vector_and_demotes_field_16()
    {
        GoldSessionPayload payload =
            GoldTestData.Builder().BuildSession(WithOptimal(1044, 120, 40, 884), GoldTestData.Ctx()).Event;

        payload.OptimalGapMs.Should().Be(1044);
        payload.SectorOptimalGapMs.Should().Equal(120, 40, 884);
        payload.TheoreticalBestGapMs.Should().BeNull("field 16 demotes under a cross-session optimal");
    }
}
