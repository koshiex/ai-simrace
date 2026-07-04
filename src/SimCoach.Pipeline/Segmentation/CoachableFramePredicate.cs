using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Segmentation;

/// <summary>
/// Frame-level coachability: a single frame belongs to a racing lap only when the sim marks the lap
/// valid and the car is out of the pit lane. Shared by the compute M1 accumulation latch (_lapPoisoned,
/// which keeps a track-limits-invalid or pit lap out of the aggregates/reference) and the fuel
/// accumulator so both agree on what counts toward stats. Note the M1 EMISSION latch (_lapInPit) is
/// stricter-scoped and gates on the pit flag ALONE, so an invalid flying lap is still coached live. This
/// predicate is deliberately NOT the
/// whole-lap <see cref="CleanLapPredicate"/> (which additionally judges tyres-off and
/// black-flag/penalty bits for reference seeding): a frame can be coachable for live tips even on a lap
/// that is not clean enough to seed a reference. Tyres-off is intentionally excluded here — it belongs
/// to off-track/clean labeling, not the emit-gate.
/// </summary>
public static class CoachableFramePredicate
{
    /// <summary>True when the frame is on a valid lap and out of the pit lane.</summary>
    public static bool IsCoachable(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.IsValidLap && !frame.IsInPitLane;
    }
}
