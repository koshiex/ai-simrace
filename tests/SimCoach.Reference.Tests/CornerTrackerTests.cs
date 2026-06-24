using FluentAssertions;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class CornerTrackerTests
{
    private static readonly Corner _corner = new()
    {
        Id = "t01",
        Name = null,
        StartPosition = 0.10f,
        ApexPosition = 0.15f,
        EndPosition = 0.20f,
    };

    [Fact]
    public void Fires_again_after_Reset()
    {
        // The per-lap re-arm is the second-order half of the live-ACC lap-boundary fix: ComputeSession
        // calls Reset() on every start-line crossing, so a tracker that fired on lap 1 must fire again
        // on lap 2. (The old crossing predicate never fired on real ACC, so this reset never ran and
        // trackers stayed latched after the first lap.)
        CornerTracker tracker = new(_corner, resumeThrottlePct: 0.5f);

        DriveCorner(tracker).Should().NotBeNull("the tracker fires on corner exit the first lap");
        tracker.Reset();
        DriveCorner(tracker).Should().NotBeNull("the tracker re-arms after Reset and fires again the next lap");
    }

    [Fact]
    public void Does_not_fire_twice_within_one_lap()
    {
        // Once latched, a second throttle stab in the same corner must not re-emit (one event per
        // corner per lap) until Reset.
        CornerTracker tracker = new(_corner, resumeThrottlePct: 0.5f);

        DriveCorner(tracker).Should().NotBeNull();
        tracker.Accept(Frame(pos: 0.18f, speedMps: 40f, throttlePct: 0.9f)).Should().BeNull(
            "the corner already fired this lap");
    }

    // Enter the window, reach minimum speed, then resume throttle past the apex → fires on exit.
    private static IReadOnlyList<TelemetryFrame>? DriveCorner(CornerTracker tracker)
    {
        tracker.Accept(Frame(pos: 0.10f, speedMps: 60f, throttlePct: 0f));
        tracker.Accept(Frame(pos: 0.14f, speedMps: 30f, throttlePct: 0f)); // minimum speed
        return tracker.Accept(Frame(pos: 0.16f, speedMps: 35f, throttlePct: 0.8f)); // past apex + on power
    }

    private static TelemetryFrame Frame(float pos, float speedMps, float throttlePct) => new()
    {
        NormalizedCarPosition = pos,
        SpeedMps = speedMps,
        ThrottlePct = throttlePct,
    };
}
