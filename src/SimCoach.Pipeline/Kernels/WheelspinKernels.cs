using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Drive-wheel wheelspin over a corner window — peak rear longitudinal <c>slip_ratio</c> during the
/// throttle-on (power-down) phase, normalized to 0..1 between an onset and a saturation threshold. Only
/// frames with throttle applied are considered, and only positive slip counts: positive longitudinal
/// slip is power-down wheelspin, whereas negative slip is braking lockup (which must NOT score as
/// wheelspin). Uses the longitudinal slip ratio (not the combined-magnitude <c>wheel_slip</c>, which is
/// lateral-contaminated mid-corner). A window with no throttle-phase rear-slip data returns 0.
/// </summary>
public static class WheelspinKernels
{
    private const int RearLeft = 2;
    private const int RearRight = 3;

    /// <summary>Throttle must be at or above this for a frame to count as the power-down phase.</summary>
    private const float ThrottleOnsetPct = 0.2f;

    /// <summary>Below this rear slip ratio there is no meaningful wheelspin (maps to 0).</summary>
    private const float OnsetSlipRatio = 0.10f;

    /// <summary>At or above this rear slip ratio the score saturates at 1.</summary>
    private const float SaturationSlipRatio = 0.40f;

    public static float WheelspinScore(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);

        float peak = 0f;
        foreach (TelemetryFrame frame in frames)
        {
            if (frame.SlipRatio.Count <= RearRight || frame.ThrottlePct < ThrottleOnsetPct)
            {
                continue;
            }

            // Signed max — positive slip only; negative (braking lockup) leaves the peak at 0.
            float rear = MathF.Max(frame.SlipRatio[RearLeft], frame.SlipRatio[RearRight]);
            if (rear > peak)
            {
                peak = rear;
            }
        }

        return Math.Clamp((peak - OnsetSlipRatio) / (SaturationSlipRatio - OnsetSlipRatio), 0f, 1f);
    }
}
