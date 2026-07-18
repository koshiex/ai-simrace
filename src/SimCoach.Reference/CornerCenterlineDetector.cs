namespace SimCoach.Reference;

/// <summary>
/// Detects corners on the aggregate median centerline (ADR-0014). The centerline is differentiated once
/// (heading + signed curvature) and fused with median |lateral g|; corners are the active stretches.
/// Close complexes are split by a per-lap-consensus rule (validated by the corner-split research): a
/// loaded stretch is cut between two prominent apexes only when the notch is deep, both apexes are
/// genuinely tight (R ≤ 180 m), AND a majority of the individual clean laps independently show the two
/// apexes — so a real chicane (left-right, every lap) splits while a single-lap line artifact does not.
/// A clear de-load gap (fused valley below the load floor) always separates corners. The apex is the
/// geometric centre of each corner's extent; the radius/trigger come from its tightest point.
/// </summary>
public static class CornerCenterlineDetector
{
    /// <summary>Curvature radius (metres) at/below which a point counts as cornering (absK·180 ≥ 1).</summary>
    public const float CornerRadiusThresholdM = 180f;

    /// <summary>Fused load at/above which the centerline is "in a corner" (R ≤ 180 m OR |G| ≥ 1.0 g).</summary>
    public const float ActiveThreshold = 1.0f;

    /// <summary>Inactive gaps shorter than this (metres) are bridged into one active run.</summary>
    public const int BridgeGapM = 45;

    /// <summary>An active run must span at least this (metres) to host a corner.</summary>
    public const int MinArcM = 35;

    /// <summary>Candidate apexes closer than this (metres) are collapsed (keep the higher load).</summary>
    public const int CollapseM = 12;

    /// <summary>A candidate apex needs at least this topographic prominence within its active run.</summary>
    public const float MinProminence = 0.30f;

    /// <summary>A fused valley below this between two apexes is a clear de-load gap → separate corners.</summary>
    public const float LoadFloor = 0.55f;

    /// <summary>To split a still-loaded stretch the valley must be below this fraction of the smaller apex.</summary>
    public const float ValleyRatio = 0.65f;

    /// <summary>Both apexes of a loaded-stretch split must reach this curvature (absK·180 ≥ 1 ⇒ R ≤ 180 m).</summary>
    public const float CurvatureFloor = 1.0f;

    /// <summary>Per-lap search half-window (metres) around each apex when confirming a split.</summary>
    public const int ConsensusWindowM = 15;

    /// <summary>Fraction of clean laps that must independently show both apexes to confirm a loaded split.</summary>
    public const float ConsensusFraction = 0.60f;

    /// <summary>A confirming per-lap apex must reach this fused load.</summary>
    public const float PerLapPeakFloor = 1.0f;

    /// <summary>Two confirming per-lap apexes must be at least this far apart (metres).</summary>
    public const int MinApexSeparationM = 12;

    /// <summary>
    /// Half-window (metres) over which sub-threshold curvature is integrated for the sustained-bend channel.
    /// Wide enough that a genuine long fast arc (Curva Grande, spa Blanchimont) accumulates a large heading
    /// change over the window, while a short gentle kink does not. Tuned against the Monza/Spa oracle.
    /// </summary>
    public const int SustainedWindowM = 110;

    /// <summary>
    /// Scales the integrated moderate curvature (radians of heading change sustained over the window) into
    /// fused load, so a long arc held at R just above <see cref="CornerRadiusThresholdM"/> reaches
    /// <see cref="ActiveThreshold"/> even with no lateral-g signal — the ghost-centerline case where fast
    /// corners would otherwise vanish (ADR-0022 / OD-B1) — while straights and short kinks stay below it.
    /// Tuned against the Monza/Spa oracle (recovers Curva Grande / spa_t02 / spa_t16 at zeroed g).
    /// </summary>
    public const float SustainedScale = 2.7f;

    private const int HeadingSpanM = 8;
    private const int SmoothRadius = 3;

    /// <summary>Detects corners using the pooled centerline as its own single "lap" (test/simple use).</summary>
    public static IReadOnlyList<DetectedCorner> Detect(MedianCenterline centerline) =>
        Detect(centerline, [centerline]);

    /// <summary>
    /// Detects corners on the pooled centerline, using each clean lap's own centerline for the per-lap
    /// consensus split rule.
    /// </summary>
    public static IReadOnlyList<DetectedCorner> Detect(
        MedianCenterline centerline, IReadOnlyList<MedianCenterline> perLapCenterlines) =>
        Detect(centerline, perLapCenterlines, SustainedScale);

    /// <summary>
    /// Core detection with an injectable sustained-bend scale. The calibration oracle drives it with 0
    /// (channel off, i.e. the pre-channel absK·180/|g| fusion) and <see cref="SustainedScale"/> (channel on)
    /// on the Monza/Spa owner centerlines: with lateral g present the two runs must agree (no regression),
    /// and with g zeroed the on-run must recover the fast corners the off-run drops (ADR-0022 / OD-B1).
    /// </summary>
    internal static IReadOnlyList<DetectedCorner> Detect(
        MedianCenterline centerline, IReadOnlyList<MedianCenterline> perLapCenterlines, float sustainedScale)
    {
        ArgumentNullException.ThrowIfNull(centerline);
        ArgumentNullException.ThrowIfNull(perLapCenterlines);

        int n = (int)MathF.Round(centerline.LapLengthM);
        if (n < (2 * HeadingSpanM) + 1)
        {
            return [];
        }

        // The sustained-bend channel only ADDS activation: it may bring a fast arc up to the active load,
        // but the split/merge topology (below) is decided on the BASE load (absK·180 / |g|, no sustained), so
        // the channel can never fill a de-load valley the base detector saw and thereby merge or fold away an
        // owner corner. This is what keeps Monza/Spa detection unchanged when lateral g is present.
        (float[] absK, float[] gs, float[] baseLoad, float[] fused) = SignalsFor(centerline, n, sustainedScale);
        List<float[]> lapBase = new(perLapCenterlines.Count);
        foreach (MedianCenterline lap in perLapCenterlines)
        {
            lapBase.Add(SignalsFor(lap, n, sustainedScale).Base);
        }

        bool[] active = new bool[n];
        for (int i = 0; i < n; i++)
        {
            active[i] = fused[i] >= ActiveThreshold;
        }

        BridgeGaps(active, BridgeGapM);
        List<(int Start, int End)> runs = FindRuns(active, MinArcM);

        // Candidate apexes per run (prominent local maxima of fused), tagged with their run.
        List<(int Pos, int RunStart, int RunEnd)> candidates = [];
        foreach ((int runStart, int runEnd) in runs)
        {
            foreach (int pos in RunApexes(fused, runStart, runEnd))
            {
                candidates.Add((pos, runStart, runEnd));
            }
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        // Left-to-right pairwise merge: keep or fold each next candidate into the current corner.
        List<(int Pos, int RunStart, int RunEnd)> kept = [candidates[0]];
        for (int c = 1; c < candidates.Count; c++)
        {
            (int Pos, int RunStart, int RunEnd) current = kept[^1];
            (int Pos, int RunStart, int RunEnd) next = candidates[c];
            float valley = MinOver(baseLoad, current.Pos, next.Pos);

            bool keep;
            if (valley < LoadFloor)
            {
                keep = true; // a clear base de-load gap always separates corners
            }
            else
            {
                bool deep = valley < ValleyRatio * MathF.Min(baseLoad[current.Pos], baseLoad[next.Pos]);
                bool tight = (absK[current.Pos] * 180f) >= CurvatureFloor && (absK[next.Pos] * 180f) >= CurvatureFloor;
                bool consensus = ConsensusFractionOf(lapBase, current.Pos, next.Pos) >= ConsensusFraction;
                keep = deep && tight && consensus;
            }

            if (keep)
            {
                kept.Add(next);
            }
            else
            {
                kept[^1] = baseLoad[current.Pos] >= baseLoad[next.Pos] ? current : next;
            }
        }

        float curvatureThreshold = 1f / CornerRadiusThresholdM;
        return EmitCorners(kept, absK, gs, fused, centerline.LapLengthM, curvatureThreshold);
    }

    private static List<DetectedCorner> EmitCorners(
        List<(int Pos, int RunStart, int RunEnd)> kept,
        float[] absK,
        float[] gs,
        float[] fused,
        float lapLengthM,
        float curvatureThreshold)
    {
        List<DetectedCorner> corners = [];
        int i = 0;
        while (i < kept.Count)
        {
            int runStart = kept[i].RunStart;
            int runEnd = kept[i].RunEnd;
            List<int> apexes = [];
            int j = i;
            while (j < kept.Count && kept[j].RunStart == runStart && kept[j].RunEnd == runEnd)
            {
                apexes.Add(kept[j].Pos);
                j++;
            }

            // Split the run at the load valley between consecutive kept apexes; each piece is one corner.
            int spanStart = runStart;
            for (int k = 0; k < apexes.Count; k++)
            {
                int spanEnd = k < apexes.Count - 1 ? ArgMinOver(fused, apexes[k], apexes[k + 1]) : runEnd;
                corners.Add(BuildCorner(absK, gs, spanStart, spanEnd, lapLengthM, curvatureThreshold));
                spanStart = spanEnd + 1;
            }

            i = j;
        }

        return corners;
    }

    private static DetectedCorner BuildCorner(
        float[] absK, float[] gs, int startIdx, int endIdx, float lapLengthM, float curvatureThreshold)
    {
        int apexIdx = (startIdx + endIdx) / 2;
        float maxKappa = absK[startIdx];
        float peakG = gs[startIdx];
        for (int i = startIdx + 1; i <= endIdx; i++)
        {
            if (absK[i] > maxKappa)
            {
                maxKappa = absK[i];
            }

            if (gs[i] > peakG)
            {
                peakG = gs[i];
            }
        }

        return new DetectedCorner
        {
            StartPosition = startIdx / lapLengthM,
            ApexPosition = apexIdx / lapLengthM,
            EndPosition = endIdx / lapLengthM,
            ApexRadiusM = maxKappa > 1e-6f ? 1f / maxKappa : float.PositiveInfinity,
            PeakLateralG = peakG,
            Trigger = Classify(maxKappa, peakG, curvatureThreshold),
        };
    }

    private static CornerChannel Classify(float apexKappa, float peakG, float curvatureThreshold)
    {
        bool byCurvature = apexKappa >= curvatureThreshold;
        bool byLoad = peakG >= ActiveThreshold;
        if (byCurvature && byLoad)
        {
            return CornerChannel.Both;
        }

        return byCurvature ? CornerChannel.Curvature : CornerChannel.LateralG;
    }

    /// <summary>Prominent local maxima of fused within [start,end]; always at least the run's global max.</summary>
    private static List<int> RunApexes(float[] fused, int start, int end)
    {
        List<int> local = [];
        for (int i = start; i <= end; i++)
        {
            int left1 = Math.Max(start, i - 1);
            int right1 = Math.Min(end, i + 1);
            int left3 = Math.Max(start, i - 3);
            int right3 = Math.Min(end, i + 3);
            if (fused[i] >= fused[left1] && fused[i] >= fused[right1] && fused[i] > fused[left3] && fused[i] > fused[right3])
            {
                local.Add(i);
            }
        }

        List<int> prominent = [];
        foreach (int peak in local)
        {
            if (RunProminence(fused, start, end, peak) >= MinProminence)
            {
                prominent.Add(peak);
            }
        }

        if (prominent.Count == 0)
        {
            prominent.Add(ArgMaxOver(fused, start, end));
        }

        List<int> collapsed = [];
        foreach (int peak in prominent)
        {
            if (collapsed.Count > 0 && (peak - collapsed[^1]) < CollapseM)
            {
                if (fused[peak] > fused[collapsed[^1]])
                {
                    collapsed[^1] = peak;
                }
            }
            else
            {
                collapsed.Add(peak);
            }
        }

        return collapsed;
    }

    private static float RunProminence(float[] fused, int start, int end, int peak)
    {
        float leftMin = fused[peak];
        int i = peak - 1;
        while (i >= start && fused[i] <= fused[peak])
        {
            leftMin = MathF.Min(leftMin, fused[i]);
            i--;
        }

        float rightMin = fused[peak];
        i = peak + 1;
        while (i <= end && fused[i] <= fused[peak])
        {
            rightMin = MathF.Min(rightMin, fused[i]);
            i++;
        }

        return fused[peak] - MathF.Max(leftMin, rightMin);
    }

    private static float ConsensusFractionOf(List<float[]> lapFused, int p1, int p2)
    {
        if (lapFused.Count == 0)
        {
            return 0f;
        }

        int confirmed = 0;
        foreach (float[] fz in lapFused)
        {
            int a = LocalMaxNear(fz, p1);
            int b = LocalMaxNear(fz, p2);
            if (a >= 0 && b >= 0 && (b - a) >= MinApexSeparationM)
            {
                confirmed++;
            }
        }

        return (float)confirmed / lapFused.Count;
    }

    private static int LocalMaxNear(float[] fz, int p)
    {
        int lo = Math.Max(3, p - ConsensusWindowM);
        int hi = Math.Min(fz.Length - 4, p + ConsensusWindowM);
        for (int i = lo; i <= hi; i++)
        {
            if (fz[i] >= PerLapPeakFloor && fz[i] >= fz[i - 1] && fz[i] >= fz[i + 1] && fz[i] > fz[i - 3] && fz[i] > fz[i + 3])
            {
                return i;
            }
        }

        return -1;
    }

    private static (float[] AbsK, float[] Gs, float[] Base, float[] Fused) SignalsFor(
        MedianCenterline centerline, int n, float sustainedScale)
    {
        (float[] x, float[] z, float[] g) = ToArrays(centerline, n);
        float[] heading = new float[n];
        for (int i = 0; i < n; i++)
        {
            int behind = Mod(i - HeadingSpanM, n);
            int ahead = Mod(i + HeadingSpanM, n);
            heading[i] = MathF.Atan2(z[ahead] - z[behind], x[ahead] - x[behind]);
        }

        float[] kappa = new float[n];
        for (int i = 0; i < n; i++)
        {
            kappa[i] = AngleDelta(heading[Mod(i + HeadingSpanM, n)], heading[Mod(i - HeadingSpanM, n)]) / (2f * HeadingSpanM);
        }

        float[] absK = SmoothCircular(Absolute(kappa), n);
        float[] gs = SmoothCircular(g, n);
        float[] sustained = SustainedBend(absK, n);
        float[] baseLoad = new float[n];
        float[] fused = new float[n];
        for (int i = 0; i < n; i++)
        {
            baseLoad[i] = MathF.Max(absK[i] * 180f, gs[i]);
            fused[i] = MathF.Max(baseLoad[i], sustained[i] * sustainedScale);
        }

        return (absK, gs, baseLoad, fused);
    }

    /// <summary>
    /// Integrated MODERATE curvature over ±<see cref="SustainedWindowM"/> metres: the total heading change a
    /// sustained fast arc turns through over the window. Points already tight (|κ|·180 ≥ 1 ⇒ R ≤
    /// <see cref="CornerRadiusThresholdM"/>) are excluded, because the instantaneous absK·180 channel already
    /// detects those — integrating only the sub-threshold curvature keeps this channel orthogonal, so it lifts
    /// a long R-just-over-threshold arc to the active load without inflating load inside tight corners/esses
    /// (which would otherwise merge them). Smoothed to a stable ridge. Fed the already-smoothed |κ|.
    /// </summary>
    private static float[] SustainedBend(float[] absK, int n)
    {
        float tightCap = 1f / CornerRadiusThresholdM;
        float[] moderate = new float[n];
        for (int i = 0; i < n; i++)
        {
            moderate[i] = absK[i] < tightCap ? absK[i] : 0f;
        }

        int w = Math.Min(SustainedWindowM, (n - 1) / 2);
        float[] integral = new float[n];
        float window = 0f;
        for (int j = -w; j <= w; j++)
        {
            window += moderate[Mod(j, n)];
        }

        integral[0] = window;
        for (int i = 1; i < n; i++)
        {
            window += moderate[Mod(i + w, n)] - moderate[Mod(i - 1 - w, n)];
            integral[i] = window;
        }

        return SmoothCircular(integral, n);
    }

    private static (float[] X, float[] Z, float[] G) ToArrays(MedianCenterline centerline, int n)
    {
        float[] x = new float[n];
        float[] z = new float[n];
        float[] g = new float[n];
        bool[] has = new bool[n];
        foreach (CenterlineBin bin in centerline.Bins)
        {
            int d = bin.DistanceM;
            if (d >= 0 && d < n)
            {
                x[d] = bin.X;
                z[d] = bin.Z;
                g[d] = bin.LateralG;
                has[d] = true;
            }
        }

        float lastX = 0f;
        float lastZ = 0f;
        float lastG = 0f;
        for (int b = 0; b < n; b++)
        {
            if (has[b])
            {
                lastX = x[b];
                lastZ = z[b];
                lastG = g[b];
            }
            else
            {
                x[b] = lastX;
                z[b] = lastZ;
                g[b] = lastG;
            }
        }

        return (x, z, g);
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

    private static float[] SmoothCircular(float[] values, int n)
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

    private static void BridgeGaps(bool[] active, int maxGapM)
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

    private static List<(int Start, int End)> FindRuns(bool[] active, int minArcM)
    {
        List<(int Start, int End)> runs = [];
        int n = active.Length;
        int start = -1;
        for (int i = 0; i < n; i++)
        {
            if (active[i] && start < 0)
            {
                start = i;
            }
            else if (!active[i] && start >= 0)
            {
                if ((i - 1 - start) >= minArcM)
                {
                    runs.Add((start, i - 1));
                }

                start = -1;
            }
        }

        if (start >= 0 && (n - 1 - start) >= minArcM)
        {
            runs.Add((start, n - 1));
        }

        return runs;
    }

    private static float MinOver(float[] values, int lo, int hi)
    {
        float min = values[lo];
        for (int i = lo + 1; i <= hi; i++)
        {
            min = MathF.Min(min, values[i]);
        }

        return min;
    }

    private static int ArgMinOver(float[] values, int lo, int hi)
    {
        int best = lo;
        for (int i = lo + 1; i <= hi; i++)
        {
            if (values[i] < values[best])
            {
                best = i;
            }
        }

        return best;
    }

    private static int ArgMaxOver(float[] values, int lo, int hi)
    {
        int best = lo;
        for (int i = lo + 1; i <= hi; i++)
        {
            if (values[i] > values[best])
            {
                best = i;
            }
        }

        return best;
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
