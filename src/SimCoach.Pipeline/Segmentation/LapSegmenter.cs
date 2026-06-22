using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Segmentation;

/// <summary>
/// Turns a frame stream into fully-bounded <see cref="CompletedLap"/>s. Stateful and pure (time comes
/// from the frame timestamp, never the wall clock): the caller drives it one frame at a time and a lap
/// surfaces only when the next start-line crossing closes it. The first and last laps of a stream are
/// partial (their start or end was never observed as a crossing) and are deliberately discarded —
/// only laps the segmenter saw begin and end qualify for reference use.
/// </summary>
public sealed class LapSegmenter
{
    private readonly List<TelemetryFrame> _current = [];
    private TelemetryFrame? _previous;
    private bool _startedAtLine;

    /// <summary>
    /// Feeds one frame; returns the lap that just completed at a start-line crossing, or <c>null</c>.
    /// </summary>
    public CompletedLap? Accept(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        CompletedLap? completed = null;

        if (_previous is not null && IsStartLineCrossing(_previous, frame))
        {
            if (_startedAtLine && _current.Count > 0)
            {
                completed = Build(_current, frame.T.ToDateTimeOffset());
            }

            _current.Clear();
            _startedAtLine = true; // the lap now beginning had its start observed
        }

        _current.Add(frame);
        _previous = frame;
        return completed;
    }

    /// <summary>
    /// A start-line crossing needs both a lap-number increment AND a normalized-position wrap (high
    /// → low). The wrap guard rejects a spurious lap-counter bump that is not a real lap completion.
    /// </summary>
    private static bool IsStartLineCrossing(TelemetryFrame previous, TelemetryFrame current) =>
        current.LapNumber > previous.LapNumber
        && current.NormalizedCarPosition < previous.NormalizedCarPosition;

    private static CompletedLap Build(List<TelemetryFrame> lapFrames, DateTimeOffset crossedAt)
    {
        TelemetryFrame[] frames = [.. lapFrames];
        var start = frames[0].T.ToDateTimeOffset();
        int lapTimeMs = (int)(crossedAt - start).TotalMilliseconds;

        return new CompletedLap
        {
            LapNumber = frames[0].LapNumber,
            LapTimeMs = lapTimeMs,
            SectorTimesMs = ComputeSectorTimes(frames, start, lapTimeMs),
            IsClean = CleanLapPredicate.IsClean(frames),
            Frames = frames,
        };
    }

    /// <summary>
    /// Sector durations from contiguous <c>current_sector_index</c> runs. Each split is measured from
    /// the lap start so per-millisecond truncation does not accumulate; the final sector takes the
    /// remainder of <paramref name="lapTimeMs"/>, so the entries always sum to the lap time exactly.
    /// </summary>
    private static IReadOnlyList<int> ComputeSectorTimes(
        IReadOnlyList<TelemetryFrame> frames, DateTimeOffset start, int lapTimeMs)
    {
        List<int> times = [];
        int currentSector = frames[0].CurrentSectorIndex;

        for (int i = 1; i < frames.Count; i++)
        {
            int sector = frames[i].CurrentSectorIndex;
            if (sector == currentSector)
            {
                continue;
            }

            // Elapsed-from-start minus what prior sectors already claimed → no cumulative drift.
            int elapsedFromStart = (int)(frames[i].T.ToDateTimeOffset() - start).TotalMilliseconds;
            times.Add(elapsedFromStart - times.Sum());
            currentSector = sector;
        }

        times.Add(lapTimeMs - times.Sum());
        return times;
    }
}
