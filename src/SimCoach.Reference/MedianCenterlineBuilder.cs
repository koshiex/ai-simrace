using SimCoach.Contracts.V1;

namespace SimCoach.Reference;

/// <summary>
/// Builds a <see cref="MedianCenterline"/> by aggregating POSITION first: for each 1-metre distance
/// bin, the median world position and median absolute lateral g across many wrap-segmented laps.
/// Reversing the old "differentiate each lap then average" order is the ADR-0014 fix — the median of
/// positions is well-conditioned and robust to single-lap outliers (teleports, off-track lines),
/// whereas a mean is poisoned by them (T7).
/// </summary>
public static class MedianCenterlineBuilder
{
    /// <summary>
    /// Minimum laps before a centerline should be trusted. With fewer, the per-bin median cannot
    /// reliably reject an outlier lap (T7). Callers gate on <see cref="MedianCenterline.LapCount"/>.
    /// </summary>
    public const int MinLapsForTrust = 3;

    /// <summary>
    /// Aggregates the given laps (each a frame slice from one wrap-bounded lap, e.g.
    /// <c>CompletedLap.Frames</c>) into a median centerline. Frames with non-positive speed or no
    /// world position are skipped (teleport / stationary guard); only the first frame per bin per lap
    /// contributes, so a slow lap cannot over-weight a bin. Gaps between sampled bins are carry-filled
    /// from the previous real bin and marked <see cref="CenterlineBin.LapSamples"/> = 0.
    /// </summary>
    public static MedianCenterline Build(
        string trackId,
        float lapLengthM,
        IReadOnlyList<IReadOnlyList<TelemetryFrame>> laps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapLengthM);
        ArgumentNullException.ThrowIfNull(laps);

        int binCount = (int)MathF.Round(lapLengthM);
        var xs = new List<float>?[binCount];
        var zs = new List<float>?[binCount];
        var gs = new List<float>?[binCount];
        int lapCount = 0;

        foreach (IReadOnlyList<TelemetryFrame> lap in laps)
        {
            if (lap is null)
            {
                continue;
            }

            bool[] seen = new bool[binCount];
            bool contributed = false;
            foreach (TelemetryFrame frame in lap)
            {
                if (frame.SpeedMps <= 0f || frame.WorldPos is null)
                {
                    continue;
                }

                int bin = (int)MathF.Floor(frame.LapDistanceM);
                if (bin < 0 || bin >= binCount || seen[bin])
                {
                    continue;
                }

                seen[bin] = true;
                contributed = true;
                (xs[bin] ??= []).Add(frame.WorldPos.X);
                (zs[bin] ??= []).Add(frame.WorldPos.Z);
                (gs[bin] ??= []).Add(frame.GForceG is null ? 0f : MathF.Abs(frame.GForceG.X));
            }

            if (contributed)
            {
                lapCount++;
            }
        }

        List<CenterlineBin> bins = new(binCount);
        float lastX = 0f;
        float lastZ = 0f;
        float lastG = 0f;
        bool haveReal = false;
        for (int bin = 0; bin < binCount; bin++)
        {
            if (xs[bin] is { Count: > 0 } binXs)
            {
                lastX = Median(binXs);
                lastZ = Median(zs[bin]!);
                lastG = Median(gs[bin]!);
                haveReal = true;
                bins.Add(new CenterlineBin
                {
                    DistanceM = bin,
                    X = lastX,
                    Z = lastZ,
                    LateralG = lastG,
                    LapSamples = binXs.Count,
                });
            }
            else if (haveReal)
            {
                // Carry-fill a gap so the path stays continuous; flagged LapSamples = 0.
                bins.Add(new CenterlineBin { DistanceM = bin, X = lastX, Z = lastZ, LateralG = lastG, LapSamples = 0 });
            }
            // A leading gap before the first real sample is dropped (the path starts at the first bin).
        }

        return new MedianCenterline
        {
            TrackId = trackId,
            LapLengthM = lapLengthM,
            LapCount = lapCount,
            Bins = bins,
        };
    }

    /// <summary>Median of the values; the mean of the two middles for an even count. Mutates (sorts) the list.</summary>
    private static float Median(List<float> values)
    {
        values.Sort();
        int n = values.Count;
        int mid = n / 2;
        return (n % 2) == 1 ? values[mid] : 0.5f * (values[mid - 1] + values[mid]);
    }
}
