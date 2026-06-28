using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Drive-wheel wheelspin over a corner window — peak rear longitudinal <c>slip_ratio</c> on exit,
/// normalized to 0..1 between an onset and a saturation threshold. Uses the longitudinal slip ratio
/// (not the combined-magnitude <c>wheel_slip</c>, which is lateral-contaminated mid-corner). A window
/// with no rear-slip data returns 0 rather than throwing.
/// </summary>
public static class WheelspinKernels
{
    private const int RearLeft = 2;
    private const int RearRight = 3;

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
            if (frame.SlipRatio.Count <= RearRight)
            {
                continue;
            }

            float rear = MathF.Max(MathF.Abs(frame.SlipRatio[RearLeft]), MathF.Abs(frame.SlipRatio[RearRight]));
            if (rear > peak)
            {
                peak = rear;
            }
        }

        return Math.Clamp((peak - OnsetSlipRatio) / (SaturationSlipRatio - OnsetSlipRatio), 0f, 1f);
    }
}
