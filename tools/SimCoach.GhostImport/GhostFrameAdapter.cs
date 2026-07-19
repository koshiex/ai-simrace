using SimCoach.Contracts.V1;

namespace SimCoach.GhostImport;

/// <summary>
/// Adapts a decoded ghost lap (<see cref="GhostRecord"/>s) into <see cref="TelemetryFrame"/>s that
/// <c>MedianCenterlineBuilder</c> can bin. A ghost carries only world XYZ + yaw + pedals — no speed,
/// no world-pos message, no lap-distance — so a naive frame (SpeedMps=0) would be skipped by the
/// builder's teleport/stationary guard, yielding an empty (silently inert) centerline.
/// </summary>
public static class GhostFrameAdapter
{
    /// <summary>
    /// Placeholder speed stamped on every ghost frame. Ghosts have no trustworthy speed channel (the
    /// +126 clock is logarithmically encoded, ADR-per <c>acc-ghost-format-re.md</c>), so a positive
    /// constant is used purely to keep frames past the builder's <c>SpeedMps &lt;= 0f</c> guard. The
    /// teleport/stationary guard is thereby INERT for ghost frames — position quality is instead
    /// established out-of-band by the import-time bbox/arithmetic guards and per-lap coherence checks.
    /// </summary>
    private const float PlaceholderSpeedMps = 1f;

    /// <summary>
    /// Projects ghost records to frames one-to-one (frame count == record count). <c>WorldPos</c> maps
    /// XYZ; <c>GForceG</c> is left null so the builder reads lateral g as 0. <c>LapDistanceM</c> is the
    /// record's OWN cumulative XZ arc-length (running sum of consecutive-segment distances, first frame
    /// = 0) — a provisional self-axis that B1b re-stamps onto a common cross-lap axis before binning.
    /// </summary>
    public static IReadOnlyList<TelemetryFrame> ToFrames(IReadOnlyList<GhostRecord> records) =>
        ToFramesWithDistances(records, CumulativeArcLengthM(records));

    /// <summary>
    /// The record's own cumulative XZ arc-length at each index — a running sum of consecutive-segment chord
    /// distances, first = 0. This is the provisional self-axis; B1b normalizes it to the catalog lap length
    /// (so the whole lap spans <c>[0, lapLength]</c>, mirroring the sim spline) before it seeds the shared axis.
    /// </summary>
    public static IReadOnlyList<float> CumulativeArcLengthM(IReadOnlyList<GhostRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        float[] distances = new float[records.Count];
        float cumulative = 0f;
        for (int i = 0; i < records.Count; i++)
        {
            if (i > 0)
            {
                float dx = records[i].WorldX - records[i - 1].WorldX;
                float dz = records[i].WorldZ - records[i - 1].WorldZ;
                cumulative += MathF.Sqrt((dx * dx) + (dz * dz));
            }

            distances[i] = cumulative;
        }

        return distances;
    }

    /// <summary>
    /// Like <see cref="ToFrames"/> but stamps each frame's <c>LapDistanceM</c> from an externally-supplied
    /// COMMON-axis distance (B1b: the record's projection onto the provisional shared centerline) instead of
    /// the record's own cumulative self-axis. World position and the null <c>GForceG</c> (→ lateral g 0) are
    /// mapped identically. Requires <paramref name="commonAxisDistancesM"/> to have one entry per record.
    /// </summary>
    public static IReadOnlyList<TelemetryFrame> ToFramesWithDistances(
        IReadOnlyList<GhostRecord> records, IReadOnlyList<float> commonAxisDistancesM)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(commonAxisDistancesM);
        if (records.Count != commonAxisDistancesM.Count)
        {
            throw new ArgumentException(
                $"expected one distance per record ({records.Count}), got {commonAxisDistancesM.Count}",
                nameof(commonAxisDistancesM));
        }

        var frames = new List<TelemetryFrame>(records.Count);
        for (int i = 0; i < records.Count; i++)
        {
            GhostRecord record = records[i];
            frames.Add(new TelemetryFrame
            {
                Sim = "acc",
                SpeedMps = PlaceholderSpeedMps,
                LapDistanceM = commonAxisDistancesM[i],
                WorldPos = new Vec3 { X = record.WorldX, Y = record.WorldY, Z = record.WorldZ },
            });
        }

        return frames;
    }
}
