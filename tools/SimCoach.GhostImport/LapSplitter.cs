namespace SimCoach.GhostImport;

/// <summary>
/// Splits a continuous decoded ghost world-path into individual laps by loop closure. The <c>.ghost</c>
/// carries no lap/normalized-position channel, so a lap boundary is inferred geometrically: the path must
/// travel at least <see cref="GhostImportOptions.LoopClosureMinTravelM"/> away from the start point and
/// then return within <see cref="GhostImportOptions.LoopClosureRadiusM"/> of it. Only complete (closed)
/// laps are returned; a trailing in-progress segment that never closes is dropped.
/// </summary>
internal static class LapSplitter
{
    internal static IReadOnlyList<IReadOnlyList<GhostRecord>> Split(
        IReadOnlyList<GhostRecord> records, GhostImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(options);

        var laps = new List<IReadOnlyList<GhostRecord>>();
        if (records.Count == 0)
        {
            return laps;
        }

        float startX = records[0].WorldX;
        float startZ = records[0].WorldZ;
        float radiusSq = options.LoopClosureRadiusM * options.LoopClosureRadiusM;
        float minTravelSq = options.LoopClosureMinTravelM * options.LoopClosureMinTravelM;

        var current = new List<GhostRecord>();
        bool travelledAway = false;
        foreach (GhostRecord record in records)
        {
            current.Add(record);
            float dx = record.WorldX - startX;
            float dz = record.WorldZ - startZ;
            float distSq = (dx * dx) + (dz * dz);
            if (distSq > minTravelSq)
            {
                travelledAway = true;
            }
            else if (travelledAway && distSq <= radiusSq)
            {
                laps.Add(current);
                current = new List<GhostRecord>();
                travelledAway = false;
            }
        }

        return laps;
    }
}
