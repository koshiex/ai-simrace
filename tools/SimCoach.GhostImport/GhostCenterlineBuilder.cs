using SimCoach.Contracts.V1;
using SimCoach.Reference;

namespace SimCoach.GhostImport;

/// <summary>
/// Builds a ghost-derived median centerline (B1b) from several complete-loop ghost laps of one track.
/// Each ghost carries its OWN cumulative arc-length total AND its own lap-start phase (<see cref="LapSplitter"/>
/// starts at <c>records[0]</c>), so naive <c>floor(LapDistanceM)</c> binning smears physically-offset points
/// across bins (apex drift, doubled/dropped corners). The fix bootstraps a SHARED axis: the fastest usable
/// ghost becomes a provisional centerline, every ghost — including the fastest, so the whole set sits on one
/// uniform basis — is projected onto it (<see cref="CenterlineAligner.ProjectDistancesM"/>) to recover a
/// common arc-length, its frames are re-stamped to that axis, and only then are all K laps median-binned.
/// Lateral g is 0 throughout (ghosts carry no g-force — ADR-0022).
/// </summary>
internal static class GhostCenterlineBuilder
{
    /// <summary>
    /// Builds the centerline from <paramref name="usableLaps"/> (each one complete loop; <c>usableLaps[0]</c>
    /// is the provisional/fastest, caller-ordered fastest-first). Requires at least
    /// <see cref="MedianCenterlineBuilder.MinLapsForTrust"/> laps. <paramref name="lapLengthM"/> sizes the
    /// shared axis (the track's catalog lap length — <c>AccTrackCatalog.TryGetLapLengthM</c>).
    /// </summary>
    internal static GhostCenterlineResult Build(
        string trackId,
        float lapLengthM,
        IReadOnlyList<IReadOnlyList<GhostRecord>> usableLaps,
        GhostImportOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapLengthM);
        ArgumentNullException.ThrowIfNull(usableLaps);
        ArgumentNullException.ThrowIfNull(options);
        if (usableLaps.Count < MedianCenterlineBuilder.MinLapsForTrust)
        {
            throw new InvalidDataException(
                $"only {usableLaps.Count} usable ghost lap(s) for '{trackId}'; "
                + $"need >= {MedianCenterlineBuilder.MinLapsForTrust}");
        }

        // Provisional shared axis: the fastest usable ghost, its driven arc-length NORMALIZED to the catalog
        // lap length so the axis spans the whole lap [0, lapLengthM] (mirrors the sim's NormalizedCarPosition·
        // length). Without normalization a driven racing line longer than the catalog spline would push the
        // tail past round(lapLengthM) bins, be silently dropped by MedianCenterlineBuilder, and read as full
        // coverage on the projected-axis span check (B-1). It is only an alignment parameterization — the
        // emitted positions are medians of every lap, so a slightly imperfect provisional line does not leak
        // into the result beyond its arc-length ordering.
        IReadOnlyList<float> drivenArcM = GhostFrameAdapter.CumulativeArcLengthM(usableLaps[0]);
        float drivenTotalM = drivenArcM[^1];
        float axisScale = drivenTotalM > 0f ? lapLengthM / drivenTotalM : 1f;
        float[] provisionalDistancesM = new float[drivenArcM.Count];
        for (int i = 0; i < drivenArcM.Count; i++)
        {
            provisionalDistancesM[i] = drivenArcM[i] * axisScale;
        }

        IReadOnlyList<TelemetryFrame> provisionalFrames =
            GhostFrameAdapter.ToFramesWithDistances(usableLaps[0], provisionalDistancesM);
        MedianCenterline provisional = MedianCenterlineBuilder.Build(trackId, lapLengthM, [provisionalFrames]);

        var reStamped = new List<IReadOnlyList<TelemetryFrame>>(usableLaps.Count);
        foreach (IReadOnlyList<GhostRecord> lap in usableLaps)
        {
            IReadOnlyList<float> commonAxis = CenterlineAligner.ProjectDistancesM(lap, provisional);
            reStamped.Add(GhostFrameAdapter.ToFramesWithDistances(lap, commonAxis));
        }

        MedianCenterline centerline = MedianCenterlineBuilder.Build(trackId, lapLengthM, reStamped);
        CoherenceReport coherence = CenterlineCoherence.Evaluate(trackId, lapLengthM, reStamped);
        float spanFraction = SpanFraction(centerline, lapLengthM);

        List<string> reasons = [];
        if (coherence.LapCount < MedianCenterlineBuilder.MinLapsForTrust)
        {
            reasons.Add($"only {coherence.LapCount} full lap(s); need >= {MedianCenterlineBuilder.MinLapsForTrust}");
        }

        if (coherence.MedianDeviationM > options.GhostCoherenceCeilingM)
        {
            reasons.Add(
                $"median-from-median deviation {coherence.MedianDeviationM:0.00} m exceeds the ghost ceiling "
                + $"{options.GhostCoherenceCeilingM:0.00} m");
        }

        if (spanFraction < options.MinGhostSpanFraction)
        {
            reasons.Add(
                $"sampled bins span only {spanFraction:0.00} of the lap; need >= {options.MinGhostSpanFraction:0.00}");
        }

        // Cross-driver line spread lifts the robust MEDIAN a few metres, but a localized geometric smear —
        // records from two arc-distant passes snapping to one co-located provisional bin where the line
        // self-approaches in XZ (a crossover, or an elevation-collapsed bridge/underpass) — spikes the worst
        // single-bin deviation into the tens of metres while the median stays low. Gate on it so that smear is
        // a loud NO-GO, not a silent corruption (B-3). Generous ceiling: normal spread never trips it.
        if (coherence.MaxDeviationM > options.GhostMaxDeviationCeilingM)
        {
            reasons.Add(
                $"worst-bin deviation {coherence.MaxDeviationM:0.00} m exceeds the max ceiling "
                + $"{options.GhostMaxDeviationCeilingM:0.00} m (possible self-approach smear)");
        }

        return new GhostCenterlineResult
        {
            Centerline = centerline,
            Coherence = coherence,
            SpanFraction = spanFraction,
            CoherenceCeilingM = options.GhostCoherenceCeilingM,
            Go = reasons.Count == 0,
            Reasons = reasons,
        };
    }

    /// <summary>
    /// Fraction of the lap length spanned by the centerline's real (<see cref="CenterlineBin.LapSamples"/> &gt; 0)
    /// bins: (last real distance − first real distance + 1) / round(lapLength). A partial-arc bake — only the
    /// first half of the lap decoded — scores low here even if its few bins are internally coherent.
    /// </summary>
    private static float SpanFraction(MedianCenterline centerline, float lapLengthM)
    {
        int binCount = (int)MathF.Round(lapLengthM);
        if (binCount <= 0)
        {
            return 0f;
        }

        int firstReal = -1;
        int lastReal = -1;
        foreach (CenterlineBin bin in centerline.Bins)
        {
            if (bin.LapSamples <= 0)
            {
                continue;
            }

            if (firstReal < 0)
            {
                firstReal = bin.DistanceM;
            }

            lastReal = bin.DistanceM;
        }

        return firstReal < 0 ? 0f : (lastReal - firstReal + 1) / (float)binCount;
    }
}
