using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Segmentation;

/// <summary>
/// Frame-level coachability: a single frame belongs to a racing lap only when the sim marks the lap
/// valid and the car is out of the pit lane. Shared by the compute emit-gate (the M1 poison latch) and
/// the fuel accumulator so both agree on what a coachable frame is. This is deliberately NOT the
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
