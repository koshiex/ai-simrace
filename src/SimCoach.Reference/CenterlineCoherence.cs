using SimCoach.Contracts.V1;

namespace SimCoach.Reference;

/// <summary>
/// The offline precondition gate for ADR-0014: decides whether a track's recorded laps agree closely
/// enough, per distance bin, that a median centerline can be trusted. For each 1-metre bin with two or
/// more laps it takes the per-lap deviation from the bin's median world position; the robust headline
/// (<see cref="CoherenceReport.MedianDeviationM"/>) is the cross-bin median of those, which a single
/// teleport/off-track lap cannot move (that lap surfaces only in <see cref="CoherenceReport.MaxDeviationM"/>).
/// Fails closed below <see cref="MedianCenterlineBuilder.MinLapsForTrust"/> laps. NOTE: this is the
/// OFFLINE half only — live NCP/lap-wrap sync remains the open precondition (ADR-0014).
/// </summary>
public static class CenterlineCoherence
{
    /// <summary>
    /// Largest cross-bin median deviation (metres) still considered trustworthy. The observed envelope
    /// is sub-metre (Spa 0.52 m, Monza 0.33–0.37 m); 1 m leaves headroom without admitting a bad bake.
    /// </summary>
    public const float MaxTrustedMedianDeviationM = 1.0f;

    /// <summary>
    /// Evaluates coherence over wrap-segmented laps (each a frame slice, e.g. <c>CompletedLap.Frames</c>).
    /// Mirrors <see cref="MedianCenterlineBuilder"/>'s binning (first frame per bin, speed/world guards).
    /// </summary>
    public static CoherenceReport Evaluate(
        string trackId,
        float lapLengthM,
        IReadOnlyList<IReadOnlyList<TelemetryFrame>> laps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lapLengthM);
        ArgumentNullException.ThrowIfNull(laps);

        int binCount = (int)MathF.Round(lapLengthM);
        var perBin = new List<(float X, float Z)>?[binCount];
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
                (perBin[bin] ??= []).Add((frame.WorldPos.X, frame.WorldPos.Z));
            }

            if (contributed)
            {
                lapCount++;
            }
        }

        List<float> binMedianDevs = [];
        float maxFromMedian = 0f;
        int binsEvaluated = 0;
        for (int bin = 0; bin < binCount; bin++)
        {
            List<(float X, float Z)>? samples = perBin[bin];
            if (samples is null || samples.Count < 2)
            {
                continue;
            }

            binsEvaluated++;
            List<float> xs = new(samples.Count);
            List<float> zs = new(samples.Count);
            foreach ((float X, float Z) sample in samples)
            {
                xs.Add(sample.X);
                zs.Add(sample.Z);
            }

            xs.Sort();
            zs.Sort();
            float mx = MedianSorted(xs);
            float mz = MedianSorted(zs);

            List<float> deviations = new(samples.Count);
            foreach ((float X, float Z) sample in samples)
            {
                float dx = sample.X - mx;
                float dz = sample.Z - mz;
                deviations.Add(MathF.Sqrt((dx * dx) + (dz * dz)));
            }

            deviations.Sort();
            binMedianDevs.Add(MedianSorted(deviations));
            maxFromMedian = MathF.Max(maxFromMedian, deviations[^1]);
        }

        binMedianDevs.Sort();
        float medianDeviation = MedianSorted(binMedianDevs);
        float p95 = Percentile(binMedianDevs, 0.95f);

        List<string> reasons = [];
        if (lapCount < MedianCenterlineBuilder.MinLapsForTrust)
        {
            reasons.Add($"only {lapCount} full lap(s); need >= {MedianCenterlineBuilder.MinLapsForTrust}");
        }

        if (binsEvaluated == 0)
        {
            reasons.Add("no bin had >= 2 laps to compare");
        }
        else if (medianDeviation > MaxTrustedMedianDeviationM)
        {
            reasons.Add($"median-from-median deviation {medianDeviation:0.00} m exceeds {MaxTrustedMedianDeviationM:0.00} m");
        }

        return new CoherenceReport
        {
            TrackId = trackId,
            LapCount = lapCount,
            BinsEvaluated = binsEvaluated,
            MedianDeviationM = medianDeviation,
            P95DeviationM = p95,
            MaxDeviationM = maxFromMedian,
            Go = reasons.Count == 0,
            Reasons = reasons,
        };
    }

    private static float MedianSorted(List<float> sorted)
    {
        int n = sorted.Count;
        if (n == 0)
        {
            return 0f;
        }

        int mid = n / 2;
        return (n % 2) == 1 ? sorted[mid] : 0.5f * (sorted[mid - 1] + sorted[mid]);
    }

    private static float Percentile(List<float> sorted, float quantile)
    {
        int n = sorted.Count;
        if (n == 0)
        {
            return 0f;
        }

        if (n == 1)
        {
            return sorted[0];
        }

        float rank = quantile * (n - 1);
        int lo = (int)MathF.Floor(rank);
        int hi = (int)MathF.Ceiling(rank);
        return sorted[lo] + ((sorted[hi] - sorted[lo]) * (rank - lo));
    }
}
