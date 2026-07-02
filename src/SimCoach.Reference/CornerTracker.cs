using SimCoach.Contracts.V1;

namespace SimCoach.Reference;

/// <summary>
/// Per-corner, in-lap state machine that buffers the frames inside a corner window and fires once the
/// car crosses the <b>geometric corner end</b> (<see cref="Corner.EndPosition"/>). The full
/// <c>[StartPosition, EndPosition]</c> span is what the self-side kernels measure (M2/M24); the
/// throttle-resume point is derived inside the kernel, not by truncating this buffer. Firing once per
/// corner per lap avoids double-emission; the buffer covers a single lap-crossing. Reset between laps.
/// </summary>
internal sealed class CornerTracker
{
    private readonly List<TelemetryFrame> _buffer = [];
    private bool _active;
    private bool _emitted;

    public CornerTracker(Corner corner) => Corner = corner;

    public Corner Corner { get; }

    /// <summary>Feeds one frame; returns the buffered corner window when the car crosses the corner end, else null.</summary>
    public IReadOnlyList<TelemetryFrame>? Accept(TelemetryFrame frame)
    {
        if (_emitted)
        {
            return null;
        }

        float pos = frame.NormalizedCarPosition;
        if (!_active)
        {
            if (pos < Corner.StartPosition || pos > Corner.EndPosition)
            {
                return null;
            }

            _active = true;
            _buffer.Clear();
        }

        _buffer.Add(frame);

        // Primary close: fire once the car has crossed the geometric corner end, so the buffer spans
        // the whole corner. A degenerate corner never reached before the start line simply never fires,
        // exactly as the old backstop behaved.
        if (pos > Corner.EndPosition)
        {
            return Fire();
        }

        return null;
    }

    public void Reset()
    {
        _active = false;
        _emitted = false;
        _buffer.Clear();
    }

    private IReadOnlyList<TelemetryFrame> Fire()
    {
        _emitted = true;
        _active = false;
        return _buffer;
    }
}
