using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Understeer / oversteer scoring — a documented heuristic proxy (see <see cref="BalanceScores"/>).
/// Scores only <em>steady-state mid-corner</em> frames (braking and longitudinal-accel phases are
/// gated out) and normalises the per-frame front/rear slip asymmetry into <c>[0,1]</c> via a
/// scale-free ratio before accumulation. The gate/scale values are named constants, flagged
/// heuristic: the inputs are native channels, the score is not, and it is advisory for coaching,
/// never a correctness gate.
/// </summary>
public static class BalanceKernels
{
    /// <summary>wheel_slip layout is [FL, FR, RL, RR]; a frame needs all four to score.</summary>
    private const int WheelCount = 4;

    /// <summary>Below this steering magnitude the car is not cornering, so balance is undefined.</summary>
    private const float CorneringSteerThresholdRad = 0.05f;

    /// <summary>
    /// Steady-state brake gate: above this <c>brake_pct</c> (0..1) the front axle carries transfer
    /// load and slips more, so a neutral car reads as understeer — skip the frame. A whisper of
    /// residual brake pressure is tolerated so mid-corner trail-off frames still score.
    /// </summary>
    private const float BrakeQuietMax = 0.05f;

    /// <summary>
    /// Steady-state longitudinal-g gate: above this |g_force_g.z| (g, z = longitudinal) the car is
    /// braking or accelerating hard enough to bias axle slip, so skip the frame. Only applied when
    /// the sim actually populates g-force — ACC often omits it (null / zero vector), in which case we
    /// degrade to the brake-only gate rather than gate every frame out.
    /// </summary>
    private const float LongGQuietMax = 0.15f;

    public static BalanceScores Analyze(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("A corner window needs at least one frame.", nameof(frames));
        }

        double understeerSum = 0;
        double oversteerSum = 0;
        int corneringFrames = 0;

        foreach (TelemetryFrame frame in frames)
        {
            if (frame.WheelSlip.Count < WheelCount || MathF.Abs(frame.SteerRad) < CorneringSteerThresholdRad)
            {
                continue;
            }

            // Steady-state gate: braking OR hard longitudinal accel loads an axle and fakes balance.
            // g-force is optional (ACC omits it), so the long-g clause only fires when it is present —
            // a null/zero vector degrades to the brake-only gate (never gates all frames to zero).
            bool braking = frame.BrakePct > BrakeQuietMax;
            bool longitudinal = frame.GForceG is not null && MathF.Abs(frame.GForceG.Z) > LongGQuietMax;
            if (braking || longitudinal)
            {
                continue;
            }

            float front = (MathF.Abs(frame.WheelSlip[0]) + MathF.Abs(frame.WheelSlip[1])) / 2f;
            float rear = (MathF.Abs(frame.WheelSlip[2]) + MathF.Abs(frame.WheelSlip[3])) / 2f;
            float sum = front + rear;

            // Scale-free asymmetry ratio |front − rear| / (front + rear): inherently [0,1], no magic
            // divisor to tune, and 0 when neither axle slips. Accumulated per-frame so the mean below
            // is bounded [0,1] by construction; the downstream understeer_trend clamp is a backstop.
            float ratio = sum > 0f ? MathF.Abs(front - rear) / sum : 0f;
            if (front > rear)
            {
                understeerSum += ratio; // fronts sliding more → understeer
            }
            else if (front < rear)
            {
                oversteerSum += ratio; // rears sliding more → oversteer
            }

            // front == rear (or both zero) is balanced: contributes to neither score, only the count.
            corneringFrames++;
        }

        if (corneringFrames == 0)
        {
            return new BalanceScores { UndersteerScore = 0f, OversteerScore = 0f };
        }

        return new BalanceScores
        {
            UndersteerScore = (float)(understeerSum / corneringFrames),
            OversteerScore = (float)(oversteerSum / corneringFrames),
        };
    }
}
