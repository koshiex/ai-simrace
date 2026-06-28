using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Steering smoothness over a corner window — the population standard deviation of steer-rate
/// (Δsteer_rad / Δt) across consecutive frames. Higher = a busier, less settled wheel. Frame pairs
/// with a non-positive Δt (duplicate timestamps) are skipped so no NaN/Inf reaches the float field.
/// Fewer than two usable pairs returns 0.
/// </summary>
public static class SteeringJitterKernels
{
    public static float SteeringJitter(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count < 2)
        {
            return 0f;
        }

        double sum = 0;
        double sumSquares = 0;
        int count = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            double dt = (frames[i].T.ToDateTimeOffset() - frames[i - 1].T.ToDateTimeOffset()).TotalSeconds;
            if (dt <= 0)
            {
                continue;
            }

            double rate = (frames[i].SteerRad - frames[i - 1].SteerRad) / dt;
            sum += rate;
            sumSquares += rate * rate;
            count++;
        }

        if (count < 2)
        {
            return 0f;
        }

        double mean = sum / count;
        double variance = (sumSquares / count) - (mean * mean);
        return variance > 0 ? (float)Math.Sqrt(variance) : 0f;
    }
}
