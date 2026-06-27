namespace SimCoach.Reference;

/// <summary>
/// Detects corners on a <see cref="MedianCenterline"/> by differentiating the aggregate path EXACTLY
/// ONCE (ADR-0014): heading is atan2 of the world-position delta over a fixed span, curvature is the
/// heading delta over the same span. Detection fuses two SIGN-STABLE channels — centerline curvature
/// (R below threshold) and median |lateral g| — so flat/large-radius corners that pure curvature would
/// miss are still found. Close complexes that the fusion gate merges are then split at a curvature
/// sign-change or a fused-load valley between two peaks. The apex is the argmax of |curvature|, never
/// the argmax of lateral g.
/// </summary>
public static class CornerCenterlineDetector
{
    /// <summary>Curvature fires the corner channel below this radius (metres).</summary>
    public const float CornerRadiusThresholdM = 180f;

    /// <summary>Median |lateral g| at or above this fires the load channel.</summary>
    public const float CornerLateralGThreshold = 1.0f;

    /// <summary>Detected arcs shorter than this (metres) are discarded as noise (applied post-split too).</summary>
    public const int MinArcM = 35;

    /// <summary>Inactive gaps shorter than this (metres) between active runs are bridged.</summary>
    public const int MergeGapM = 45;

    /// <summary>Minimum spacing (metres) between two load peaks for them to be split candidates.</summary>
    public const int MinPeakSeparationM = 40;

    /// <summary>Signed curvature beyond ±this (rad/m) each side of a valley counts as a sign reversal (~R250 — a real direction change, not noise).</summary>
    public const float SplitSignedCurvatureThreshold = 0.004f;

    /// <summary>A valley below this fraction of the smaller flanking peak splits the complex.</summary>
    public const float SplitValleyFraction = 0.65f;

    /// <summary>Each flanking peak must reach this fused load to be a split candidate (kills phantom entry-load splits).</summary>
    public const float MinSplitPeakLoad = 1.25f;

    /// <summary>Minimum peak-to-valley fused-load drop to split (prominence; stops over-splitting one corner).</summary>
    public const float MinSplitProminence = 0.35f;

    private const int HeadingSpanM = 8;
    private const int SmoothRadius = 3;

    /// <summary>Detects corners on the centerline, in ascending position order.</summary>
    public static IReadOnlyList<DetectedCorner> Detect(MedianCenterline centerline)
    {
        ArgumentNullException.ThrowIfNull(centerline);
        IReadOnlyList<CenterlineBin> bins = centerline.Bins;
        int n = bins.Count;
        if (n < (2 * HeadingSpanM) + 1)
        {
            return [];
        }

        float[] rawKappa = SignedCurvature(bins);
        float[] signedKappa = Smooth(rawKappa, n);
        float[] absKappa = Smooth(Absolute(rawKappa), n);
        float[] latG = Smooth(LateralGate(bins), n);

        float curvatureThreshold = 1f / CornerRadiusThresholdM;
        float[] fusedLoad = new float[n];
        for (int i = 0; i < n; i++)
        {
            fusedLoad[i] = MathF.Max(absKappa[i] / curvatureThreshold, latG[i] / CornerLateralGThreshold);
        }

        bool[] active = new bool[n];
        for (int i = 0; i < n; i++)
        {
            active[i] = fusedLoad[i] >= 1f;
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
                EmitSegment(corners, bins, absKappa, signedKappa, latG, fusedLoad, curvatureThreshold, start, i - 1, centerline.LapLengthM);
                start = -1;
            }
        }

        if (start >= 0)
        {
            EmitSegment(corners, bins, absKappa, signedKappa, latG, fusedLoad, curvatureThreshold, start, n - 1, centerline.LapLengthM);
        }

        return corners;
    }

    private static void EmitSegment(
        List<DetectedCorner> corners,
        IReadOnlyList<CenterlineBin> bins,
        float[] absKappa,
        float[] signedKappa,
        float[] latG,
        float[] fusedLoad,
        float curvatureThreshold,
        int startIdx,
        int endIdx,
        float lapLengthM)
    {
        if (bins[endIdx].DistanceM - bins[startIdx].DistanceM < MinArcM)
        {
            return;
        }

        foreach ((int Start, int End) range in Split(startIdx, endIdx, fusedLoad, signedKappa))
        {
            if (bins[range.End].DistanceM - bins[range.Start].DistanceM < MinArcM)
            {
                continue;
            }

            corners.Add(BuildCorner(bins, absKappa, latG, curvatureThreshold, range.Start, range.End, lapLengthM));
        }
    }

    private static DetectedCorner BuildCorner(
        IReadOnlyList<CenterlineBin> bins,
        float[] absKappa,
        float[] latG,
        float curvatureThreshold,
        int startIdx,
        int endIdx,
        float lapLengthM)
    {
        // Apex = geometric centre of the corner extent (ADR-0014): line-independent, so a single driver's
        // early-apex line does not drag it toward the entry. Radius/trigger use the tightest point in the window.
        int apexIdx = (startIdx + endIdx) / 2;
        float maxKappa = absKappa[startIdx];
        float peakG = latG[startIdx];
        for (int i = startIdx + 1; i <= endIdx; i++)
        {
            if (absKappa[i] > maxKappa)
            {
                maxKappa = absKappa[i];
            }

            if (latG[i] > peakG)
            {
                peakG = latG[i];
            }
        }

        return new DetectedCorner
        {
            StartPosition = bins[startIdx].DistanceM / lapLengthM,
            ApexPosition = bins[apexIdx].DistanceM / lapLengthM,
            EndPosition = bins[endIdx].DistanceM / lapLengthM,
            ApexRadiusM = maxKappa > 1e-6f ? 1f / maxKappa : float.PositiveInfinity,
            PeakLateralG = peakG,
            Trigger = Classify(maxKappa, peakG, curvatureThreshold),
        };
    }

    private static List<(int Start, int End)> Split(int startIdx, int endIdx, float[] fusedLoad, float[] signedKappa)
    {
        List<int> peaks = FindPeaks(fusedLoad, startIdx, endIdx);
        if (peaks.Count < 2)
        {
            return [(startIdx, endIdx)];
        }

        List<int> cuts = [];
        for (int p = 0; p < peaks.Count - 1; p++)
        {
            int left = peaks[p];
            int right = peaks[p + 1];
            int valley = left;
            float maxSigned = float.NegativeInfinity;
            float minSigned = float.PositiveInfinity;
            for (int i = left; i <= right; i++)
            {
                if (fusedLoad[i] < fusedLoad[valley])
                {
                    valley = i;
                }

                maxSigned = MathF.Max(maxSigned, signedKappa[i]);
                minSigned = MathF.Min(minSigned, signedKappa[i]);
            }

            bool signReverses = maxSigned > SplitSignedCurvatureThreshold && minSigned < -SplitSignedCurvatureThreshold;
            bool valleyDeep = fusedLoad[valley] < SplitValleyFraction * MathF.Min(fusedLoad[left], fusedLoad[right]);
            bool peaksAreReal = fusedLoad[left] >= MinSplitPeakLoad && fusedLoad[right] >= MinSplitPeakLoad;
            bool prominentEnough = (MathF.Min(fusedLoad[left], fusedLoad[right]) - fusedLoad[valley]) >= MinSplitProminence;
            if ((signReverses || valleyDeep) && peaksAreReal && prominentEnough)
            {
                cuts.Add(valley);
            }
        }

        if (cuts.Count == 0)
        {
            return [(startIdx, endIdx)];
        }

        List<(int Start, int End)> ranges = [];
        int rangeStart = startIdx;
        foreach (int cut in cuts)
        {
            ranges.Add((rangeStart, cut));
            rangeStart = cut + 1;
        }

        ranges.Add((rangeStart, endIdx));
        return ranges;
    }

    private static List<int> FindPeaks(float[] fusedLoad, int startIdx, int endIdx)
    {
        List<int> peaks = [];
        for (int i = startIdx; i <= endIdx; i++)
        {
            if (fusedLoad[i] < 1f)
            {
                continue;
            }

            bool risesFromLeft = i == startIdx || fusedLoad[i] > fusedLoad[i - 1];
            bool fallsToRight = i == endIdx || fusedLoad[i] > fusedLoad[i + 1];
            bool plateauTop = (i == startIdx || fusedLoad[i] >= fusedLoad[i - 1]) && (i == endIdx || fusedLoad[i] >= fusedLoad[i + 1]);
            if (!plateauTop || !(risesFromLeft || fallsToRight))
            {
                continue;
            }

            if (peaks.Count > 0 && (i - peaks[^1]) < MinPeakSeparationM)
            {
                if (fusedLoad[i] > fusedLoad[peaks[^1]])
                {
                    peaks[^1] = i;
                }

                continue;
            }

            peaks.Add(i);
        }

        return peaks;
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

    private static float[] Absolute(float[] values)
    {
        float[] result = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = MathF.Abs(values[i]);
        }

        return result;
    }

    private static float[] Smooth(float[] values, int n)
    {
        float[] result = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0f;
            for (int j = -SmoothRadius; j <= SmoothRadius; j++)
            {
                sum += values[Mod(i + j, n)];
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
