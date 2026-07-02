namespace SimCoach.Pipeline.Kernels;

/// <summary>
/// Pure phase-band geometry for a baked corner window (Start → Apex → End). The apex-band arithmetic
/// lives here ONCE so the live corner-phase gate (<c>SimCoach.Coach</c>'s <c>CornerPhaseResolver</c>) and
/// the brake-overlap metric (<c>SimCoach.Reference</c>'s <c>CornerEventBuilder</c>) share a single
/// definition of "apex" and cannot drift in code. Floats, not the <c>Corner</c> record (which lives in
/// Reference). Wrap-around is folded with <see cref="Mod1"/>, matching the resolver's original fold.
/// </summary>
public static class CornerPhaseBands
{
    /// <summary>Forward distance in [0, 1) — folds a raw position delta into the lap's wrap-around.</summary>
    public static double Mod1(double value) => ((value % 1.0) + 1.0) % 1.0;

    /// <summary>
    /// The corner's phase boundaries in OFFSET coordinates (distance forward from <paramref name="start"/>),
    /// derived from the baked Start/Apex/End and the apex-band <paramref name="apexBandFraction"/>. A
    /// degenerate window (length ≤ 0) yields the all-zero <see cref="CornerPhaseOffsets"/>.
    /// </summary>
    public static CornerPhaseOffsets Offsets(double start, double apex, double end, double apexBandFraction)
    {
        double length = Mod1(end - start);
        if (length <= 0)
        {
            return default;
        }

        double apexOffset = Mod1(apex - start);
        double apexStart = apexOffset * (1.0 - apexBandFraction);
        double apexEnd = apexOffset + ((length - apexOffset) * apexBandFraction);
        return new CornerPhaseOffsets(length, apexStart / 2.0, apexStart, apexEnd);
    }

    /// <summary>
    /// The turn-in → apex overlap window as ABSOLUTE normalized positions <c>[Lo, Hi]</c> — the
    /// phase-scoped span the brake-overlap metric is measured over: from turn-in start (the Braking→Entry
    /// boundary) through the apex band end (the Apex→Exit boundary). Returned as raw
    /// <c>start + offset</c> (NOT wrap-folded) to match the caller's <c>[start, end]</c> frame-slicing,
    /// which compares raw positions; a corner straddling the start/finish line inherits that same
    /// non-wrapping limitation. A degenerate corner yields an empty band (<c>Lo == Hi == start</c>).
    /// </summary>
    public static (float Lo, float Hi) TurnInToApexBand(double start, double apex, double end, double apexBandFraction)
    {
        CornerPhaseOffsets offsets = Offsets(start, apex, end, apexBandFraction);
        return ((float)(start + offsets.TurnInStart), (float)(start + offsets.ApexEnd));
    }
}
