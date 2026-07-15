using SimCoach.Contracts.V1;

namespace SimCoach.Reference;

/// <summary>
/// Rolls per-corner losses up across a session into a bounded <c>aggregated_losses</c> list for the
/// debrief (B2). Fed each corner's <see cref="CornerContribution"/> by <see cref="ComputeSession"/>;
/// only contributions with a real, reference-quantified loss (<c>DeltaMs &gt; 0</c>) count, mirroring
/// <c>ComputeSession.TopLosses</c> — so a session with no reference yields an empty list. Mutation is
/// isolated here; <see cref="Build"/> returns an immutable snapshot. <c>corner_id</c> only — the human
/// <c>corner_name</c> is resolved at the Coach layer (ADR-0010), never in compute.
///
/// The per-channel diagnostic diffs are folded abs-then-average behind the SAME <c>DeltaMs &gt; 0</c>
/// gate as the loss roll-up (ADR-0020, decisions 1 + 6): a non-lossy contribution early-returns before
/// its diffs reach the sums, so <see cref="DiffAverages"/> is conditioned on the lossy-corner set, not a
/// true all-corner average.
/// </summary>
internal sealed class SessionLossAccumulator
{
    // M36 dominant_channel closed set — the THREE SIGNED diagnostic channels only. The unsigned RMS
    // line-deviation is DELIBERATELY absent (excluded from the argmax domain, ADR-0020 / MF-2).
    private const string BrakePointChannel = "brake_point";
    private const string ThrottleResumeChannel = "throttle_resume";
    private const string MinSpeedChannel = "min_speed";

    private readonly ChannelLossScales _scales;

    public SessionLossAccumulator(ChannelLossScales scales)
    {
        _scales = scales;
    }

    private sealed class CornerLosses
    {
        public long TotalLossMs { get; set; }
        public int SampleCount { get; set; }
        public Dictionary<string, int> ReasonCounts { get; } = [];
        public double AbsBrakePointDiffSum { get; set; }
        public double AbsThrottleResumeDiffSum { get; set; }
        public double AbsMinSpeedDiffSum { get; set; }
        public double AbsLineDeviationSum { get; set; }
    }

    private readonly Dictionary<string, CornerLosses> _byCorner = [];

    public void Accept(CornerContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (contribution.DeltaMs <= 0)
        {
            return;
        }

        if (!_byCorner.TryGetValue(contribution.CornerId, out CornerLosses? losses))
        {
            losses = new CornerLosses();
            _byCorner[contribution.CornerId] = losses;
        }

        losses.TotalLossMs += contribution.DeltaMs;
        losses.SampleCount++;
        string reason = contribution.Reason;
        losses.ReasonCounts[reason] = losses.ReasonCounts.GetValueOrDefault(reason) + 1;

        // abs-then-average: fold the absolute per-corner diff so a same-magnitude early/late pair does not
        // cancel (ADR-0020 decision 1). The averaging happens in DiffAverages over SampleCount.
        losses.AbsBrakePointDiffSum += Math.Abs(contribution.BrakePointDiffM);
        losses.AbsThrottleResumeDiffSum += Math.Abs(contribution.ThrottleResumeDiffM);
        losses.AbsMinSpeedDiffSum += Math.Abs(contribution.MinSpeedDiffKmh);
        losses.AbsLineDeviationSum += Math.Abs(contribution.RacingLineDeviationM);
    }

    /// <summary>
    /// The corner's per-channel abs-then-average diagnostic diffs over its accumulated lossy samples, or
    /// <c>default</c> (all zero) for a corner that never took a lossy contribution. Conditioned on the
    /// <c>DeltaMs &gt; 0</c> gate in <see cref="Accept"/> (ADR-0020 decision 6).
    /// </summary>
    internal ChannelDiffAverages DiffAverages(string cornerId)
    {
        ArgumentNullException.ThrowIfNull(cornerId);
        if (!_byCorner.TryGetValue(cornerId, out CornerLosses? losses) || losses.SampleCount == 0)
        {
            return default;
        }

        return Averages(losses);
    }

    private static ChannelDiffAverages Averages(CornerLosses losses) =>
        new(
            (float)(losses.AbsBrakePointDiffSum / losses.SampleCount),
            (float)(losses.AbsThrottleResumeDiffSum / losses.SampleCount),
            (float)(losses.AbsMinSpeedDiffSum / losses.SampleCount),
            (float)(losses.AbsLineDeviationSum / losses.SampleCount));

    public IReadOnlyList<AggregatedLoss> Build(int topN)
    {
        if (topN <= 0)
        {
            return [];
        }

        return _byCorner
            .Select(pair => BuildLoss(pair.Key, pair.Value))
            .OrderByDescending(loss => loss.TotalLossMs)
            .ThenBy(loss => loss.CornerId, StringComparer.Ordinal)
            .Take(topN)
            .ToList();
    }

    private AggregatedLoss BuildLoss(string cornerId, CornerLosses losses)
    {
        // The diagnostic diffs 6-9 (ADR-0020) are report-only abs-then-average magnitudes over the same
        // lossy-corner samples the totals roll up from — never summed into total_loss_ms.
        ChannelDiffAverages diffs = Averages(losses);
        (string dominantChannel, int dominantValue) = DominantChannel(diffs);
        return new AggregatedLoss
        {
            CornerId = cornerId,
            TotalLossMs = (int)losses.TotalLossMs,
            AvgLossMs = (int)(losses.TotalLossMs / losses.SampleCount),
            SampleCount = losses.SampleCount,
            DominantReason = DominantReason(losses.ReasonCounts),
            AvgBrakePointDiffM = diffs.BrakePointDiffM,
            AvgThrottleResumeDiffM = diffs.ThrottleResumeDiffM,
            AvgMinSpeedDiffKmh = diffs.MinSpeedDiffKmh,
            AvgLineDeviationM = diffs.LineDeviationM,
            DominantChannel = dominantChannel,
            DominantChannelValue = dominantValue,
        };
    }

    /// <summary>
    /// M36 scaled cross-unit argmax over the THREE SIGNED channels only (brake-point, throttle-resume,
    /// min-speed). Each channel's abs-then-average diff is scaled onto a common millisecond axis by its
    /// <see cref="ChannelLossScales"/> factor, then the largest wins. The unsigned RMS line-deviation is
    /// NOT a candidate (MF-2). Ties resolve deterministically brake-point &gt; throttle-resume &gt; min-speed.
    /// Returns <c>("", 0)</c> when no signed channel has a non-zero scaled magnitude. The value is a
    /// heuristic ranking magnitude (scaled ms), never an additive time.
    /// </summary>
    private (string Channel, int Value) DominantChannel(ChannelDiffAverages diffs)
    {
        float brake = diffs.BrakePointDiffM * _scales.MsPerMetreBrakePoint;
        float throttle = diffs.ThrottleResumeDiffM * _scales.MsPerMetreThrottleResume;
        float minSpeed = diffs.MinSpeedDiffKmh * _scales.MsPerKmhMinSpeed;

        float max = MathF.Max(brake, MathF.Max(throttle, minSpeed));
        if (max <= 0f)
        {
            return (string.Empty, 0);
        }

        if (max == brake)
        {
            return (BrakePointChannel, (int)MathF.Round(brake));
        }

        return max == throttle
            ? (ThrottleResumeChannel, (int)MathF.Round(throttle))
            : (MinSpeedChannel, (int)MathF.Round(minSpeed));
    }

    private static string DominantReason(Dictionary<string, int> counts) =>
        counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .First()
            .Key;
}
