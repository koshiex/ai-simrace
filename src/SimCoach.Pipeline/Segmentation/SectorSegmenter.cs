using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Segmentation;

/// <summary>
/// Emits a <see cref="SectorSplit"/> each time <c>current_sector_index</c> changes (every track, per
/// ADR-0010 sectors always come from the sim). Stateful and pure; the caller drives it frame by frame.
/// The split reports the sector that just <em>ended</em> and the time spent in it.
/// </summary>
public sealed class SectorSegmenter
{
    private TelemetryFrame? _previous;
    private DateTimeOffset _sectorStart;

    /// <summary>Feeds one frame; returns the sector that just closed, or <c>null</c>.</summary>
    public SectorSplit? Accept(TelemetryFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (_previous is null)
        {
            _sectorStart = frame.T.ToDateTimeOffset();
            _previous = frame;
            return null;
        }

        SectorSplit? split = null;
        if (frame.CurrentSectorIndex != _previous.CurrentSectorIndex)
        {
            var crossed = frame.T.ToDateTimeOffset();
            split = new SectorSplit
            {
                LapNumber = _previous.LapNumber,
                SectorIndex = _previous.CurrentSectorIndex,
                SectorTimeMs = (int)(crossed - _sectorStart).TotalMilliseconds,
            };
            _sectorStart = crossed;
        }

        _previous = frame;
        return split;
    }
}
