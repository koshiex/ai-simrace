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

        return new ChannelDiffAverages(
            (float)(losses.AbsBrakePointDiffSum / losses.SampleCount),
            (float)(losses.AbsThrottleResumeDiffSum / losses.SampleCount),
            (float)(losses.AbsMinSpeedDiffSum / losses.SampleCount),
            (float)(losses.AbsLineDeviationSum / losses.SampleCount));
    }

    public IReadOnlyList<AggregatedLoss> Build(int topN)
    {
        if (topN <= 0)
        {
            return [];
        }

        return _byCorner
            .Select(pair => new AggregatedLoss
            {
                CornerId = pair.Key,
                TotalLossMs = (int)pair.Value.TotalLossMs,
                AvgLossMs = (int)(pair.Value.TotalLossMs / pair.Value.SampleCount),
                SampleCount = pair.Value.SampleCount,
                DominantReason = DominantReason(pair.Value.ReasonCounts),
            })
            .OrderByDescending(loss => loss.TotalLossMs)
            .ThenBy(loss => loss.CornerId, StringComparer.Ordinal)
            .Take(topN)
            .ToList();
    }

    private static string DominantReason(Dictionary<string, int> counts) =>
        counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .First()
            .Key;
}
