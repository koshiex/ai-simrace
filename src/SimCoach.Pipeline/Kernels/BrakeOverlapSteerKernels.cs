using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Unwanted brake-while-steering overlap over a corner window: the fraction of <b>all</b> window frames
/// that carry both meaningful brake and meaningful steering. Distinct from
/// <see cref="BrakeKernels"/>'s trail-brake metric, which is over braking frames only — this flags
/// overlap relative to the whole corner, so a brief correct trail-brake is small while sustained
/// braking deep into the turn is large. An empty window returns 0.
/// </summary>
public static class BrakeOverlapSteerKernels
{
    /// <summary>Minimum brake for a frame to count as braking.</summary>
    private const float BrakeThresholdPct = 0.10f;

    /// <summary>Minimum steering magnitude for a frame to count as steering.</summary>
    private const float SteerThresholdRad = 0.10f;

    public static float OverlapPct(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            return 0f;
        }

        int overlap = 0;
        foreach (TelemetryFrame frame in frames)
        {
            if (frame.BrakePct > BrakeThresholdPct && MathF.Abs(frame.SteerRad) > SteerThresholdRad)
            {
                overlap++;
            }
        }

        return (float)overlap / frames.Count;
    }
}
