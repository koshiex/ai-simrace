using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Front-wheel brake lockup over a corner window — peak FRONT longitudinal <c>slip_ratio</c> in the
/// LOCKING sign (negative = the wheel is under-rotating relative to the ground) while the brake is hard
/// on, normalized to 0..1 between an onset and a saturation magnitude. Only high-brake frames count (a
/// lockup is a braking event, not a coasting one) and only FRONT wheels (indices FL/FR) — a locked front
/// is what pushes the car straight and kills turn-in. Uses the longitudinal slip ratio, not the
/// combined-magnitude <c>wheel_slip</c> (field 20), which cannot separate lockup from wheelspin, and NOT
/// the rear wheels (rear lockup is a different, oversteer-side fault). The peak reading is ATTENUATED when
/// ABS was engaged on that frame (<c>abs_active</c> or a non-zero raw <c>abs</c> level): an ABS-equipped
/// GT3 rarely fully locks, so a large slip under ABS intervention is far more likely cyclic modulation
/// than a genuine sustained lockup, and must not read like one. A window with no hard-brake front-slip
/// data returns 0.
/// </summary>
public static class BrakeLockupKernels
{
    private const int FrontLeft = 0;
    private const int FrontRight = 1;

    /// <summary>Brake must be at or above this for a frame to count as a braking (lockup-capable) phase.</summary>
    private const float BrakeOnsetPct = 0.5f;

    /// <summary>Below this front lock magnitude there is no meaningful lockup (maps to 0).</summary>
    private const float OnsetSlipRatio = 0.10f;

    /// <summary>At or above this front lock magnitude the raw score saturates at 1.</summary>
    private const float SaturationSlipRatio = 0.40f;

    /// <summary>
    /// Multiplier applied to the raw score when ABS was engaged on the peak-lock frame — a locked reading
    /// under ABS is discounted (rarely a true lockup on a GT3), but not zeroed (ABS can still be overwhelmed).
    /// </summary>
    private const float AbsAttenuation = 0.35f;

    public static float BrakeLockupScore(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        float peakLock = 0f;
        bool peakUnderAbs = false;
        foreach (TelemetryFrame frame in frames)
        {
            if (frame.SlipRatio.Count <= FrontRight || frame.BrakePct < BrakeOnsetPct)
            {
                continue;
            }

            // Locking = the most-negative front slip (under-rotating wheel). Negate so a stronger lockup is a
            // larger positive magnitude; a rolling/spinning front (>= 0 slip) contributes nothing.
            float lockMagnitude = -MathF.Min(frame.SlipRatio[FrontLeft], frame.SlipRatio[FrontRight]);
            if (lockMagnitude > peakLock)
            {
                peakLock = lockMagnitude;
                peakUnderAbs = frame.AbsActive || frame.Abs > 0;
            }
        }

        float raw = Math.Clamp((peakLock - OnsetSlipRatio) / (SaturationSlipRatio - OnsetSlipRatio), 0f, 1f);
        return peakUnderAbs ? raw * AbsAttenuation : raw;
    }
}
