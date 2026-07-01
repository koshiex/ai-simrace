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
    /// <summary>Previous-frame position must be at least this (near the lap end) for a wrap to count.</summary>
    private const float WrapHighThreshold = 0.9f;

    /// <summary>Current-frame position must be below this (near the lap start) for a wrap to count.</summary>
    private const float WrapLowThreshold = 0.3f;

    private readonly List<TelemetryFrame> _current = [];
    private TelemetryFrame? _previous;
    private bool _startedAtLine;

    // Session-local monotonic lap numbering. The sim's lap counter (frame.LapNumber) resets on a
    // pit-return out-lap (ESC → box → drive out), so it re-issues a number already completed this
    // session — which would collide on the laps table's UNIQUE(session_id, lap_number) and crash
    // compute. Instead of echoing it, we keep a per-stint OFFSET: within a stint the assigned label is
    // the intrinsic counter plus a constant (so it stays tied to the per-frame value and is robust to
    // dropped frames, exactly like the raw counter was), and the offset only re-bases when the counter
    // fails to advance — producing a continuous sequence across pits (…2, 3, [pit] 4, 5, 6…).
    private int _lapOffset;
    private int? _lastAssigned;

    /// <summary>True when the frame just fed to <see cref="Accept"/> was a start-line crossing (whether or
    /// not it closed a bounded lap). The compute session reads this to re-arm its per-lap accumulators, so
    /// the crossing definition lives in one place instead of being duplicated and drifting.</summary>
    public bool CrossedThisFrame { get; private set; }

    /// <summary>
    /// Count of position resets into the start band (<c>pos &lt; <see cref="WrapLowThreshold"/></c>) that did
    /// NOT come from the lap end — a pit/teleport reset or a dropped recording chunk, neither of which is a
    /// lap crossing. Zero on a clean live session; a non-zero count flags a recording artifact to the caller.
    /// </summary>
    public int SuspiciousResetsIgnored { get; private set; }

    /// <summary>
    /// Feeds one frame; returns the lap that just completed at a start-line crossing, or <c>null</c>.
    /// </summary>
    public CompletedLap? Accept(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        CompletedLap? completed = null;
        CrossedThisFrame = false;

        if (_previous is not null)
        {
            if (IsStartLineCrossing(_previous, frame))
            {
                CrossedThisFrame = true;
                if (_startedAtLine && _current.Count > 0)
                {
                    completed = Build(_current, frame.T.ToDateTimeOffset());
                }

                _current.Clear();
                _startedAtLine = true; // the lap now beginning had its start observed
            }
            else if (IsSuspiciousReset(_previous, frame))
            {
                SuspiciousResetsIgnored++;
            }
        }

        _current.Add(frame);
        _previous = frame;
        return completed;
    }

    /// <summary>
    /// A start-line crossing is a wrap of normalized car position from the lap end (≈1.0) back to the
    /// start (≈0.0). lap_number is deliberately NOT part of the trigger: on live ACC the completedLaps
    /// counter increments roughly a frame before the position wraps — and stays pinned at 1.0 on the
    /// increment frame — so the old "lap-bump AND wrap on the same frame" predicate never fired and a
    /// whole session segmented to zero laps (KB: acc-lap-boundary-timing). The out-lap → lap-1 crossing
    /// never increments the counter at all, so wrap-primary also keeps the driver's first flying lap.
    /// The high/low band makes the trigger self-debouncing: a second crossing needs the previous frame
    /// back above <see cref="WrapHighThreshold"/>, which only happens after nearly a full lap — so a
    /// pit/teleport reset (which drops position from mid-lap, with the previous frame below
    /// <see cref="WrapHighThreshold"/>) cannot mint a phantom lap.
    /// </summary>
    private static bool IsStartLineCrossing(TelemetryFrame previous, TelemetryFrame current) =>
        // 0.9 clears every real wrap with ~460× margin (live ACC ~400 Hz: max frame-to-frame position
        // step 0.0002, last pre-line sample = 1.0) yet sits far above every pit/box reset origin (≤0.17).
        previous.NormalizedCarPosition > WrapHighThreshold
        && current.NormalizedCarPosition < WrapLowThreshold;

    /// <summary>A drop into the start band that did not originate at the lap end — a reset, not a crossing.</summary>
    private static bool IsSuspiciousReset(TelemetryFrame previous, TelemetryFrame current) =>
        current.NormalizedCarPosition < WrapLowThreshold
        && current.NormalizedCarPosition < previous.NormalizedCarPosition
        && previous.NormalizedCarPosition <= WrapHighThreshold;

    private CompletedLap Build(List<TelemetryFrame> lapFrames, DateTimeOffset crossedAt)
    {
        TelemetryFrame[] frames = [.. lapFrames];
        var start = frames[0].T.ToDateTimeOffset();
        int lapTimeMs = (int)(crossedAt - start).TotalMilliseconds;

        return new CompletedLap
        {
            LapNumber = AssignLapNumber(frames[0].LapNumber),
            LapTimeMs = lapTimeMs,
            SectorTimesMs = ComputeSectorTimes(frames, start, lapTimeMs),
            IsClean = CleanLapPredicate.IsClean(frames),
            Frames = frames,
        };
    }

    /// <summary>
    /// Maps the sim's (resettable) lap counter to a session-local monotonic label via a per-stint
    /// offset. The first emitted lap inherits the sim's value as the base; thereafter the offset
    /// re-bases whenever the offset-adjusted number fails to advance past the last one — i.e. when the
    /// sim counter <b>decreases or repeats</b> (the pit-return case). The <c>&lt;=</c> test is load-bearing:
    /// a repeated-equal counter would otherwise collide on <c>UNIQUE(session_id, lap_number)</c> just like
    /// a decrease. On a normal session the counter is strictly increasing, so no re-base happens and the
    /// label equals the intrinsic counter exactly (numbering is unchanged).
    /// </summary>
    private int AssignLapNumber(int intrinsic)
    {
        int natural = intrinsic + _lapOffset;
        if (_lastAssigned is not null && natural <= _lastAssigned)
        {
            _lapOffset = _lastAssigned.Value + 1 - intrinsic;
            natural = intrinsic + _lapOffset;
        }

        _lastAssigned = natural;
        return natural;
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
