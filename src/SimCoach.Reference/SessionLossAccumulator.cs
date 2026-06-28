using SimCoach.Contracts.V1;

namespace SimCoach.Reference;

/// <summary>
/// Rolls per-corner losses up across a session into a bounded <c>aggregated_losses</c> list for the
/// debrief (B2). Fed each corner's <see cref="CornerContribution"/> by <see cref="ComputeSession"/>;
/// only contributions with a real, reference-quantified loss (<c>DeltaMs &gt; 0</c>) count, mirroring
/// <c>ComputeSession.TopLosses</c> — so a session with no reference yields an empty list. Mutation is
/// isolated here; <see cref="Build"/> returns an immutable snapshot. <c>corner_id</c> only — the human
/// <c>corner_name</c> is resolved at the Coach layer (ADR-0010), never in compute.
/// </summary>
internal sealed class SessionLossAccumulator
{
    private sealed class CornerLosses
    {
        public long TotalLossMs { get; set; }
        public int SampleCount { get; set; }
        public Dictionary<string, int> ReasonCounts { get; } = [];
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
