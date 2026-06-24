using SimCoach.Contracts.V1;

namespace SimCoach.Reference;

/// <summary>
/// Per-corner, in-lap state machine that buffers the frames inside a corner window and fires once at
/// the <b>corner-exit trigger</b>: the first sustained throttle resume after the minimum-speed point
/// (mirrors the C4 throttle-on definition), with the window-end crossing as a backstop. Firing once
/// per corner per lap avoids double-emission on mid-corner throttle stabs. Reset between laps.
/// </summary>
internal sealed class CornerTracker
{
    private readonly float _resumeThrottlePct;
    private readonly List<TelemetryFrame> _buffer = [];
    private bool _active;
    private bool _emitted;
    private int _minIndex;
    private float _minSpeed;

    public CornerTracker(Corner corner, float resumeThrottlePct)
    {
        Corner = corner;
        _resumeThrottlePct = resumeThrottlePct;
    }

    public Corner Corner { get; }

    /// <summary>Feeds one frame; returns the buffered corner window when the exit trigger fires, else null.</summary>
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
            _minSpeed = float.MaxValue;
            _minIndex = 0;
        }

        _buffer.Add(frame);
        int index = _buffer.Count - 1;
        if (frame.SpeedMps < _minSpeed)
        {
            _minSpeed = frame.SpeedMps;
            _minIndex = index;
        }

        bool pastMinSpeed = index > _minIndex;
        if (pastMinSpeed && frame.ThrottlePct >= _resumeThrottlePct)
        {
            return Fire();
        }

        // Backstop: the car left the window without a clear throttle resume (flat/long corner).
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
