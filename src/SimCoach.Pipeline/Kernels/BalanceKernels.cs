using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Understeer / oversteer scoring — a documented heuristic proxy (see <see cref="BalanceScores"/>).
/// Compares front vs rear wheel-slip magnitude over cornering frames. The score formula and its
/// thresholds are named constants, flagged heuristic: the inputs are native channels, the score
/// is not, and it is advisory for coaching, never a correctness gate.
/// </summary>
public static class BalanceKernels
{
    /// <summary>wheel_slip layout is [FL, FR, RL, RR]; a frame needs all four to score.</summary>
    private const int WheelCount = 4;

    /// <summary>Below this steering magnitude the car is not cornering, so balance is undefined.</summary>
    private const float CorneringSteerThresholdRad = 0.05f;

    public static BalanceScores Analyze(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            throw new ArgumentException("A corner window needs at least one frame.", nameof(frames));
        }

        double understeerSum = 0;
        double oversteerSum = 0;
        int corneringFrames = 0;

        foreach (TelemetryFrame frame in frames)
        {
            if (frame.WheelSlip.Count < WheelCount || MathF.Abs(frame.SteerRad) < CorneringSteerThresholdRad)
            {
                continue;
            }

            float front = (MathF.Abs(frame.WheelSlip[0]) + MathF.Abs(frame.WheelSlip[1])) / 2f;
            float rear = (MathF.Abs(frame.WheelSlip[2]) + MathF.Abs(frame.WheelSlip[3])) / 2f;
            float delta = front - rear;
            if (delta > 0f)
            {
                understeerSum += delta; // fronts sliding more → understeer
            }
            else if (delta < 0f)
            {
                oversteerSum += -delta; // rears sliding more → oversteer
            }

            // delta == 0 is balanced: contributes to neither score, only to the frame count.
            corneringFrames++;
        }

        if (corneringFrames == 0)
        {
            return new BalanceScores { UndersteerScore = 0f, OversteerScore = 0f };
        }

        return new BalanceScores
        {
            UndersteerScore = (float)(understeerSum / corneringFrames),
            OversteerScore = (float)(oversteerSum / corneringFrames),
        };
    }
}
