namespace SimCoach.Reference;

/// <summary>
/// Detects corners on a <see cref="MedianCenterline"/> by differentiating the aggregate path EXACTLY
/// ONCE (ADR-0014): heading is atan2 of the world-position delta over a fixed span, curvature is the
/// heading delta over the same span. Detection fuses two SIGN-STABLE channels — centerline curvature
/// (R below threshold) and median |lateral g| — so flat/large-radius corners that pure curvature would
/// miss are still found. The apex is the argmax of |curvature|, never the argmax of lateral g.
/// This stage produces the baseline (merged-complex) corners; splitting close complexes is layered on
/// top separately.
/// </summary>
public static class CornerCenterlineDetector
{
    /// <summary>Curvature fires the corner channel below this radius (metres).</summary>
    public const float CornerRadiusThresholdM = 180f;

    /// <summary>Median |lateral g| at or above this fires the load channel.</summary>
    public const float CornerLateralGThreshold = 1.0f;

    /// <summary>Detected arcs shorter than this (metres) are discarded as noise.</summary>
    public const int MinArcM = 35;

    /// <summary>Inactive gaps shorter than this (metres) between active runs are bridged.</summary>
    public const int MergeGapM = 45;

    private const int HeadingSpanM = 8;
    private const int SmoothRadius = 3;

    /// <summary>Detects baseline corners on the centerline, in ascending position order.</summary>
    public static IReadOnlyList<DetectedCorner> Detect(MedianCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(centerline);
        IReadOnlyList<CenterlineBin> bins = centerline.Bins;
        int n = bins.Count;
        if (n < (2 * HeadingSpanM) + 1)
        {
            return [];
        }

        float[] absKappa = Smooth(SignedCurvature(bins), n, takeAbsoluteFirst: true);
        float[] latG = Smooth(LateralGate(bins), n, takeAbsoluteFirst: false);
        float curvatureThreshold = 1f / CornerRadiusThresholdM;

        bool[] active = new bool[n];
        for (int i = 0; i < n; i++)
        {
            active[i] = absKappa[i] >= curvatureThreshold || latG[i] >= CornerLateralGThreshold;
        }

        CloseSmallGaps(active, MergeGapM);

        List<DetectedCorner> corners = [];
        int start = -1;
        for (int i = 0; i < n; i++)
        {
            if (active[i] && start < 0)
            {
                start = i;
            }
            else if (!active[i] && start >= 0)
            {
                TryAddCorner(corners, bins, absKappa, latG, curvatureThreshold, start, i - 1, centerline.LapLengthM);
                start = -1;
            }
        }

        if (start >= 0)
        {
            TryAddCorner(corners, bins, absKappa, latG, curvatureThreshold, start, n - 1, centerline.LapLengthM);
        }

        return corners;
    }

    private static void TryAddCorner(
        List<DetectedCorner> corners,
        IReadOnlyList<CenterlineBin> bins,
        float[] absKappa,
        float[] latG,
        float curvatureThreshold,
        int startIdx,
        int endIdx,
        float lapLengthM)
    {
        if (bins[endIdx].DistanceM - bins[startIdx].DistanceM < MinArcM)
        {
            return;
        }

        int apexIdx = startIdx;
        float peakG = latG[startIdx];
        for (int i = startIdx + 1; i <= endIdx; i++)
        {
            if (absKappa[i] > absKappa[apexIdx])
            {
                apexIdx = i;
            }

            if (latG[i] > peakG)
            {
                peakG = latG[i];
            }
        }

        float apexKappa = absKappa[apexIdx];
        corners.Add(new DetectedCorner
        {
            StartPosition = bins[startIdx].DistanceM / lapLengthM,
            ApexPosition = bins[apexIdx].DistanceM / lapLengthM,
            EndPosition = bins[endIdx].DistanceM / lapLengthM,
            ApexRadiusM = apexKappa > 1e-6f ? 1f / apexKappa : float.PositiveInfinity,
            PeakLateralG = peakG,
            Trigger = Classify(apexKappa, peakG, curvatureThreshold),
        });
    }

    private static CornerChannel Classify(float apexKappa, float peakG, float curvatureThreshold)
    {
        bool byCurvature = apexKappa >= curvatureThreshold;
        bool byLoad = peakG >= CornerLateralGThreshold;
        if (byCurvature && byLoad)
        {
            return CornerChannel.Both;
        }

        return byCurvature ? CornerChannel.Curvature : CornerChannel.LateralG;
    }

    private static float[] SignedCurvature(IReadOnlyList<CenterlineBin> bins)
    {
        int n = bins.Count;
        float[] heading = new float[n];
        for (int i = 0; i < n; i++)
        {
            CenterlineBin behind = bins[Mod(i - HeadingSpanM, n)];
            CenterlineBin ahead = bins[Mod(i + HeadingSpanM, n)];
            heading[i] = MathF.Atan2(ahead.Z - behind.Z, ahead.X - behind.X);
        }

        float[] kappa = new float[n];
        for (int i = 0; i < n; i++)
        {
            float forward = heading[Mod(i + HeadingSpanM, n)];
            float backward = heading[Mod(i - HeadingSpanM, n)];
            kappa[i] = AngleDelta(forward, backward) / (2f * HeadingSpanM);
        }

        return kappa;
    }

    private static float[] LateralGate(IReadOnlyList<CenterlineBin> bins)
    {
        int n = bins.Count;
        float[] g = new float[n];
        for (int i = 0; i < n; i++)
        {
            g[i] = bins[i].LateralG;
        }

        return g;
    }

    private static float[] Smooth(float[] values, int n, bool takeAbsoluteFirst)
    {
        float[] result = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0f;
            for (int j = -SmoothRadius; j <= SmoothRadius; j++)
            {
                float value = values[Mod(i + j, n)];
                sum += takeAbsoluteFirst ? MathF.Abs(value) : value;
            }

            result[i] = sum / ((2 * SmoothRadius) + 1);
        }

        return result;
    }

    private static void CloseSmallGaps(bool[] active, int maxGapM)
    {
        int n = active.Length;
        int i = 0;
        while (i < n)
        {
            if (active[i])
            {
                i++;
                continue;
            }

            int j = i;
            while (j < n && !active[j])
            {
                j++;
            }

            if (i > 0 && j < n && (j - i) < maxGapM)
            {
                for (int k = i; k < j; k++)
                {
                    active[k] = true;
                }
            }

            i = j;
        }
    }

    private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;

    private static float AngleDelta(float a, float b)
    {
        float d = a - b;
        while (d > MathF.PI)
        {
            d -= 2f * MathF.PI;
        }

        while (d < -MathF.PI)
        {
            d += 2f * MathF.PI;
        }

        return d;
    }
}
