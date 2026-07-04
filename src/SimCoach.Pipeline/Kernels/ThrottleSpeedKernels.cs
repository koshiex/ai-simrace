using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Pure speed/throttle kernels over a buffered corner window: the minimum-speed point and the
/// throttle-resume point after it. A full-throttle window simply resumes throttle at the start.
/// </summary>
public static class ThrottleSpeedKernels
{
    /// <summary>Throttle at or above this counts as the driver back on power.</summary>
    private const float ThrottleResumePct = 0.5f;

    public static CornerMetrics Analyze(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("A corner window needs at least one frame.", nameof(frames));
        }

        int minIndex = 0;
        float minSpeed = frames[0].SpeedMps;
        for (int i = 1; i < frames.Count; i++)
        {
            if (frames[i].SpeedMps < minSpeed)
            {
                minSpeed = frames[i].SpeedMps;
                minIndex = i;
            }
        }

        // Throttle-resume is the first sustained throttle at or after the minimum-speed point
        // (inclusive: if the driver is already on power at the apex, that is the resume point).
        float? throttleOn = null;
        for (int i = minIndex; i < frames.Count; i++)
        {
            if (frames[i].ThrottlePct >= ThrottleResumePct)
            {
                throttleOn = frames[i].NormalizedCarPosition;
                break;
            }
        }

        // A true in-span minimum sits strictly between the endpoints and dips below both — a real
        // deceleration apex rather than a flat or monotonic transit through the window.
        bool hasInSpanMinimum = minIndex > 0
            && minIndex < frames.Count - 1
            && minSpeed < frames[0].SpeedMps
            && minSpeed < frames[^1].SpeedMps;

        return new CornerMetrics
        {
            MinSpeedMps = minSpeed,
            MinSpeedPosition = frames[minIndex].NormalizedCarPosition,
            HasInSpanMinimum = hasInSpanMinimum,
            ThrottleOnPosition = throttleOn,
        };
    }
}
