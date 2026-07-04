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
        CornerTracker tracker = new(_corner, upstreamNormalized: 0f);

        DriveCorner(tracker).Should().NotBeNull("the tracker fires on corner exit the first lap");
        tracker.Reset();
        DriveCorner(tracker).Should().NotBeNull("the tracker re-arms after Reset and fires again the next lap");
    }

    [Fact]
    public void Does_not_fire_twice_within_one_lap()
    {
        // Once latched, a second throttle stab in the same corner must not re-emit (one event per
        // corner per lap) until Reset.
        CornerTracker tracker = new(_corner, upstreamNormalized: 0f);

        DriveCorner(tracker).Should().NotBeNull();
        tracker.Accept(Frame(pos: 0.18f, speedMps: 40f, throttlePct: 0.9f)).Should().BeNull(
            "the corner already fired this lap");
    }

    [Fact]
    public void Arms_upstream_of_the_start_and_buffers_the_braking_zone()
    {
        // M16: with a non-zero upstream distance the tracker arms before StartPosition, so a frame that
        // brakes ahead of the geometric start is buffered and reaches the fired window (the brake-onset
        // scan needs it). A zero-upstream tracker would drop that same frame.
        CornerTracker tracker = new(_corner, upstreamNormalized: 0.05f);

        tracker.Accept(Frame(pos: 0.06f, speedMps: 55f, throttlePct: 0f)); // upstream of the 0.10 start
        tracker.Accept(Frame(pos: 0.10f, speedMps: 40f, throttlePct: 0f));
        tracker.Accept(Frame(pos: 0.15f, speedMps: 30f, throttlePct: 0.2f));
        IReadOnlyList<TelemetryFrame>? window = tracker.Accept(Frame(pos: 0.22f, speedMps: 45f, throttlePct: 0.9f));

        window.Should().NotBeNull();
        window!.Should().Contain(f => f.NormalizedCarPosition < _corner.StartPosition,
            "the upstream pre-roll frame is buffered for the brake-onset scan");
    }

    // Enter the window, reach minimum speed, resume throttle, then cross the geometric corner end →
    // fires on the first frame past EndPosition (the throttle stab no longer triggers an early fire).
    private static IReadOnlyList<TelemetryFrame>? DriveCorner(CornerTracker tracker)
    {
        tracker.Accept(Frame(pos: 0.10f, speedMps: 60f, throttlePct: 0f));
        tracker.Accept(Frame(pos: 0.14f, speedMps: 30f, throttlePct: 0f)); // minimum speed
        tracker.Accept(Frame(pos: 0.16f, speedMps: 35f, throttlePct: 0.8f)); // past apex + on power, still in window
        return tracker.Accept(Frame(pos: 0.22f, speedMps: 45f, throttlePct: 0.9f)); // crossed EndPosition → fires
    }

    private static TelemetryFrame Frame(float pos, float speedMps, float throttlePct) => new()
    {
        NormalizedCarPosition = pos,
        SpeedMps = speedMps,
        ThrottlePct = throttlePct,
    };
}
