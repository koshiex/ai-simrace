namespace SimCoach.Reference;

/// <summary>
/// Aggregates a session's per-sector, per-crossing reference deltas into one representative value.
/// Uses the MEDIAN of the crossings (already coachable-lap-filtered upstream — see M1) rather than the
/// arithmetic mean: a single anomalous crossing dragged the mean into implausible session aggregates
/// (the inverted "-14.8s S1", where a slow out-lap crossing averaged with a flying lap). The result
/// still populates <c>SessionEvent.sector_avg_delta_ms</c> (proto field 14); only the estimator
/// changed — the field name keeps saying "avg" for wire compatibility (rename is a MAJOR, out of scope).
/// </summary>
internal static class SectorDeltaAggregator
{
    /// <summary>
    /// Median of the deltas. For an even count returns the mean of the two middle values with C#
    /// integer division (truncation toward zero); the sub-millisecond bias is below coaching tolerance.
    /// Does not mutate the input. Precondition: <paramref name="deltas"/> is non-empty.
    /// </summary>
    public static int Median(IReadOnlyList<int> deltas)
    {
        int[] sorted = [.. deltas];
        Array.Sort(sorted);
        int n = sorted.Length;
        int mid = n / 2;
        return (n % 2) == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
