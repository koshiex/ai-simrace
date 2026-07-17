using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// Provenance JSON (PR-B3 commit 21 / OD1): the <c>alien_line</c> audit payload records the source car,
/// laptime, accreplay lap id, and track — and NEVER a driver name. The accreplay leaderboard entry carries
/// no name field at all, so no persisted artifact can leak one.
/// </summary>
public sealed class GhostProvenanceTests
{
    [Fact]
    public void FromAccReplay_records_source_car_laptime_lap_id_and_track()
    {
        var provenance = GhostProvenance.FromAccReplay(
            new AccReplayLap(3010828, "ferrari_296_gt3", 100030), "monza");

        provenance.Source.Should().Be("accreplay");
        provenance.LapId.Should().Be(3010828);
        provenance.SourceCar.Should().Be("ferrari_296_gt3");
        provenance.LapTimeMs.Should().Be(100030);
        provenance.TrackId.Should().Be("monza");
    }

    [Fact]
    public void ToJson_round_trips_and_carries_no_driver_name()
    {
        var provenance = GhostProvenance.FromAccReplay(
            new AccReplayLap(3010828, "ferrari_296_gt3", 100030), "monza");

        string json = provenance.ToJson();

        // Auditable payload present...
        json.Should().Contain("ferrari_296_gt3").And.Contain("3010828").And.Contain("monza");
        // ...and no name field can ever appear (dropped at parse — OD1).
        json.ToLowerInvariant().Should().NotContain("name").And.NotContain("driver");

        using var document = JsonDocument.Parse(json);
        document.RootElement.TryGetProperty("SourceCar", out _).Should().BeTrue();
        document.RootElement.EnumerateObject().Select(p => p.Name)
            .Should().NotContain(n => n.Contains("river", StringComparison.OrdinalIgnoreCase));
    }
}
