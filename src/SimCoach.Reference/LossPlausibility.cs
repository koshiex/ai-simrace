namespace SimCoach.Reference;

/// <summary>
/// Pure plausibility filter for emitted corner/sector time losses (M3), sitting between detection and
/// phrasing so a fabricated number never reaches the Russian text — even if the M1 poison latch or the
/// M2 span alignment regress. Mutation-free and threshold-parameterised (the thresholds live in
/// <see cref="ComputeOptions"/>, never here), so it is golden-testable in isolation and independent of
/// the M1/M2 wiring it backstops.
/// </summary>
internal static class LossPlausibility
{
    /// <summary>
    /// Tier A — absolute ceiling. A corner (or per-crossing sector) delta whose magnitude exceeds the
    /// ceiling is physically implausible and is rejected regardless of sign, so a gain rendered as a loss
    /// (e.g. <c>delta -3929 ms</c>, which <c>corner_catch_all</c> voices as a 3929 ms loss) is caught the
    /// same as an oversized loss. The lap deficit is unknown at corner/sector cadence, so this is the only
    /// tier available mid-lap.
    /// </summary>
    public static bool WithinCeiling(int deltaMs, int ceilingMs) => Math.Abs((long)deltaMs) <= ceilingMs;

    /// <summary>
    /// Tier B — deficit-relative budget. A loss is plausible only when its magnitude fits the lap's own
    /// deficit budget, <c>max(ratio × |lapDeficitMs|, floorMs)</c>. Keyed on magnitude versus the lap
    /// DEFICIT, never the sector absolute time: a <c>+14799 ms</c> sector "loss" on a lap that actually
    /// gained 1381 ms is dropped because 14799 dwarfs the 1381 budget, while the <c>14799 &lt; 35994</c>
    /// sector-time trap is avoided since the sector absolute is never the comparand. The floor stops a
    /// near-zero deficit from collapsing the budget and admits genuinely small losses.
    /// </summary>
    public static bool WithinDeficit(int deltaMs, int lapDeficitMs, float ratio, int floorMs) =>
        Math.Abs((long)deltaMs) <= DeficitBudgetMs(lapDeficitMs, ratio, floorMs);

    private static long DeficitBudgetMs(int lapDeficitMs, float ratio, int floorMs) =>
        Math.Max((long)(ratio * Math.Abs((long)lapDeficitMs)), floorMs);
}
