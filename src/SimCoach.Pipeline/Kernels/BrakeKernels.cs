using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Pure braking kernels over a buffered corner window. No <see cref="TimeProvider"/>, no allocation
/// of state — every value derives from the frames. A window with no braking returns a zero/null
/// profile rather than throwing.
/// </summary>
public static class BrakeKernels
{
    /// <summary>Brake rises past this to count as "on the brakes".</summary>
    private const float BrakeOnThresholdPct = 0.15f;

    /// <summary>Brake must fall below this (hysteresis band under <see cref="BrakeOnThresholdPct"/>) to count as released.</summary>
    private const float BrakeOffThresholdPct = 0.05f;

    /// <summary>Minimum brake for a frame to be considered part of the braking phase (trail-brake denominator).</summary>
    private const float TrailBrakeBrakeThresholdPct = 0.10f;

    /// <summary>Minimum steering magnitude for a braking frame to count as trail-braking.</summary>
    private const float TrailBrakeSteerThresholdRad = 0.10f;

    public static BrakeProfile Analyze(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("A corner window needs at least one frame.", nameof(frames));
        }

        float peak = 0f;
        float? onPosition = null;
        float? offPosition = null;
        bool braking = false;
        int brakingFrames = 0;
        int trailBrakingFrames = 0;

        foreach (TelemetryFrame frame in frames)
        {
            float brake = frame.BrakePct;
            if (brake > peak)
            {
                peak = brake;
            }

            if (!braking && brake >= BrakeOnThresholdPct)
            {
                braking = true;
                onPosition ??= frame.NormalizedCarPosition;
            }
            else if (braking && brake <= BrakeOffThresholdPct)
            {
                braking = false;
                offPosition = frame.NormalizedCarPosition;
            }

            if (brake > TrailBrakeBrakeThresholdPct)
            {
                brakingFrames++;
                if (MathF.Abs(frame.SteerRad) > TrailBrakeSteerThresholdRad)
                {
                    trailBrakingFrames++;
                }
            }
        }

        return new BrakeProfile
        {
            PeakBrakePct = peak,
            BrakeOnPosition = onPosition,
            BrakeOffPosition = offPosition,
            TrailBrakePct = brakingFrames > 0 ? (float)trailBrakingFrames / brakingFrames : 0f,
        };
    }
}
