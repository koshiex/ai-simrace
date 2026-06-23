using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;
using SimCoach.Pipeline.Segmentation;

namespace SimCoach.Reference;

/// <summary>
/// Derives a nameless <see cref="TrackModel"/> from the driver's fastest clean lap — the fallback for
/// tracks the landmark dataset does not cover (ADR-0010). The corner-detection scan is NEW logic here:
/// <see cref="BrakeKernels"/>/<see cref="ThrottleSpeedKernels"/> only measure a pre-supplied window,
/// they do not find corners. This walks the lap by position, slices each braking zone into a window
/// (brake-on → release → throttle-resume), then runs those kernels to place the corner. The thresholds
/// mirror the kernels' own constants; corner geometry from a single lap is advisory, not a correctness
/// gate (risk register).
/// </summary>
public static class TrackModelBuilder
{
    /// <summary>Brake rises past this to open a corner window (mirrors <c>BrakeKernels</c> brake-on).</summary>
    private const float BrakeOnThresholdPct = 0.15f;

    /// <summary>Brake falls below this to count as released (mirrors <c>BrakeKernels</c> brake-off).</summary>
    private const float BrakeOffThresholdPct = 0.05f;

    /// <summary>Throttle at/above this closes a window (mirrors <c>ThrottleSpeedKernels</c> resume).</summary>
    private const float ThrottleResumePct = 0.5f;

    /// <summary>
    /// Builds a derived model from a clean lap's frames. The caller guarantees the lap is clean and
    /// fully bounded (one ascending 0→1 position pass). Corners are ordered by position and IDed
    /// <c>&lt;trackId&gt;_t01..NN</c>; the count is deterministic for a given lap.
    /// </summary>
    public static TrackModel Build(string trackId, CompletedLap cleanLap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentNullException.ThrowIfNull(cleanLap);

        IReadOnlyList<TelemetryFrame> frames = cleanLap.Frames;
        List<Corner> corners = [];
        int index = 0;
        while (index < frames.Count)
        {
            if (frames[index].BrakePct < BrakeOnThresholdPct)
            {
                index++;
                continue;
            }

            (int end, int next) = FindWindowEnd(frames, index);
            corners.Add(BuildCorner(trackId, corners.Count + 1, Slice(frames, index, end)));
            index = next;
        }

        return new TrackModel
        {
            TrackId = trackId,
            Corners = corners,
            Source = TrackModelSource.Derived,
            DerivedFromLapTimeMs = cleanLap.LapTimeMs,
        };
    }

    /// <summary>
    /// From a brake-on onset, the window runs until sustained throttle resumes after the brake has
    /// released. Returns the inclusive end index and the index to resume scanning from.
    /// </summary>
    private static (int End, int Next) FindWindowEnd(IReadOnlyList<TelemetryFrame> frames, int onset)
    {
        bool released = false;
        for (int i = onset + 1; i < frames.Count; i++)
        {
            if (frames[i].BrakePct <= BrakeOffThresholdPct)
            {
                released = true;
            }

            if (released && frames[i].ThrottlePct >= ThrottleResumePct)
            {
                return (i, i + 1);
            }
        }

        int last = frames.Count - 1;
        return (last, last + 1);
    }

    private static Corner BuildCorner(string trackId, int ordinal, IReadOnlyList<TelemetryFrame> window)
    {
        BrakeProfile brake = BrakeKernels.Analyze(window);
        CornerMetrics speed = ThrottleSpeedKernels.Analyze(window);

        float start = brake.BrakeOnPosition ?? window[0].NormalizedCarPosition;
        float end = speed.ThrottleOnPosition ?? window[^1].NormalizedCarPosition;
        return new Corner
        {
            Id = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{trackId}_t{ordinal:00}"),
            Name = null,
            StartPosition = start,
            ApexPosition = speed.MinSpeedPosition,
            EndPosition = end,
        };
    }

    private static IReadOnlyList<TelemetryFrame> Slice(IReadOnlyList<TelemetryFrame> frames, int start, int endInclusive)
    {
        var window = new TelemetryFrame[endInclusive - start + 1];
        for (int i = 0; i < window.Length; i++)
        {
            window[i] = frames[start + i];
        }

        return window;
    }
}
