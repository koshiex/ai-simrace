using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.TestKit;

namespace SimCoach.Reference.Tests;

/// <summary>Shared helpers: turn a synthetic track into fully-bounded clean laps for model derivation.</summary>
internal static class TestLaps
{
    /// <summary>The fastest clean, fully-bounded lap from a synthesized multi-lap session.</summary>
    public static CompletedLap FastestClean(SyntheticTrack track, int lapCount = 4)
    {
        LapSegmenter segmenter = new();
        List<CompletedLap> laps = [];
        foreach (TelemetryFrame frame in SyntheticSessionBuilder.Build(track, lapCount))
        {
            CompletedLap? completed = segmenter.Accept(frame);
            if (completed is not null)
            {
                laps.Add(completed);
            }
        }

        return laps.Where(l => l.IsClean).OrderBy(l => l.LapTimeMs).First();
    }

    /// <summary>A clean lap reusing another lap's frames but with a chosen lap time (idempotency tests).</summary>
    public static CompletedLap WithLapTime(CompletedLap source, int lapTimeMs) => source with
    {
        LapTimeMs = lapTimeMs,
        IsClean = true,
    };
}
