using SimCoach.Contracts.V1;

namespace SimCoach.Pipeline.Segmentation;

/// <summary>
/// Decides whether a lap is "clean" enough to seed a reference (C7) — from mapped channels only,
/// no proxies (the prior wheels-off heuristic is replaced by the sim's own <c>is_valid_lap</c>).
/// Boundedness (start-line to start-line) is guaranteed upstream by <see cref="LapSegmenter"/>, so
/// this predicate only judges the in-lap channels.
/// </summary>
public static class CleanLapPredicate
{
    // flags_active bit layout (telemetry.proto): bit 2 = black flag, bit 5 = penalty.
    private const int BlackFlagBit = 1 << 2;
    private const int PenaltyBit = 1 << 5;
    private const int DisqualifyingFlags = BlackFlagBit | PenaltyBit;

    /// <summary>True when every frame is valid, on-track, out of the pit lane, and free of black-flag/penalty bits.</summary>
    public static bool IsClean(IReadOnlyList<TelemetryFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
        {
            return false;
        }

        foreach (TelemetryFrame frame in frames)
        {
            // A pit-lane touch disqualifies the whole lap: it diverges from the racing line and must
            // never seed a reference. This mirrors the fuel gate (ComputeSession) which already skips
            // pit frames; CornerEventBuilder.OffTrack labeling deliberately stays pit-agnostic (M27).
            if (!frame.IsValidLap || frame.IsInPitLane || frame.TyresOut != 0 || (frame.FlagsActive & DisqualifyingFlags) != 0)
            {
                return false;
            }
        }

        return true;
    }
}
