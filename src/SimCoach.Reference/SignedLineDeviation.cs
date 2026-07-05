using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;
using SimCoach.Storage;

namespace SimCoach.Reference;

/// <summary>One phase band as an absolute normalized-position range <c>[Lo, Hi]</c>.</summary>
internal readonly record struct PhaseBand(float Lo, float Hi);

/// <summary>
/// Signed per-phase line deviation (M34, ADR-0018): the median signed perpendicular distance of the self
/// world path from the reference line over a phase band. Positive = self runs WIDER (outside) the
/// reference line, negative = TIGHTER (inside). The sign is folded by the reference path's own turn
/// direction, so it reads the same for left- and right-handers and is invariant to the world-frame
/// handedness. A near-straight band (ambiguous turn direction) or a band with no usable frames yields 0.
/// Pure over caller-supplied bands — the corner-type gate (M38) sits on top, not here.
/// </summary>
internal static class SignedLineDeviation
{
    // Net tangent rotation below this is treated as straight: the inside/outside side is undefined, so the
    // deviation neutralises to 0.
    private const float MinTurnRotation = 0.05f;

    // Folds the perpendicular so positive is the OUTSIDE of the corner. `turnSign * cross(tangent, offset)`
    // is negative for an outside self (handedness-invariant), so it is negated.
    private const float OutsideSign = -1f;

    /// <summary>
    /// The entry/apex/exit bands as absolute <c>[Lo, Hi]</c> normalized positions, contiguous over
    /// <c>[start, end]</c>: entry = before the apex band, apex = the apex band, exit = after it. Reuses the
    /// shared <see cref="CornerPhaseBands"/> apex arithmetic (one definition of "apex").
    /// </summary>
    public static (PhaseBand Entry, PhaseBand Apex, PhaseBand Exit) EntryApexExitBands(
        double start, double apex, double end, double apexBandFraction)
    {
        CornerPhaseOffsets o = CornerPhaseBands.Offsets(start, apex, end, apexBandFraction);
        float apexStart = (float)(start + o.ApexStart);
        float apexEnd = (float)(start + o.ApexEnd);
        return (
            new PhaseBand((float)start, apexStart),
            new PhaseBand(apexStart, apexEnd),
            new PhaseBand(apexEnd, (float)(start + o.Length)));
    }

    /// <summary>
    /// Median signed perpendicular offset (metres) of the self frames inside <paramref name="band"/> from
    /// the reference line. 0 when the band is empty, degenerate, or geometrically near-straight.
    /// </summary>
    public static float MedianSignedOffset(
        IReadOnlyList<TelemetryFrame> selfFrames, ResampledLap reference, PhaseBand band)
    {
        float turnSign = TurnSign(reference, band);
        if (turnSign == 0f)
        {
            return 0f;
        }

        List<float> offsets = [];
        foreach (TelemetryFrame frame in selfFrames)
        {
            float pos = frame.NormalizedCarPosition;
            if (pos < band.Lo || pos > band.Hi)
            {
                continue;
            }

            // M43 sentinel: a torn/slot-less world position (null or the (0,0,0) sentinel) carries no line
            // information and must not fold a phantom origin distance into the offset.
            Vec3? worldPos = frame.WorldPos;
            if (worldPos is null || (worldPos.X == 0f && worldPos.Y == 0f && worldPos.Z == 0f))
            {
                continue;
            }

            (float tx, float tz) = GridMetrics.InterpWorldTangent(reference, pos);
            if (tx == 0f && tz == 0f)
            {
                continue;
            }

            (float refX, float refZ) = GridMetrics.InterpWorldXZ(reference, pos);
            float dx = worldPos.X - refX;
            float dz = worldPos.Z - refZ;
            float perpendicular = (tx * dz) - (tz * dx); // 2D cross of travel tangent with (self - ref)
            offsets.Add(OutsideSign * turnSign * perpendicular);
        }

        return Median(offsets);
    }

    // The reference tangent's net rotation across the band gives the turn direction; its sign folds
    // "outside" to positive. Near-zero rotation → straight → 0 (side undefined).
    private static float TurnSign(ResampledLap reference, PhaseBand band)
    {
        (float sx, float sz) = GridMetrics.InterpWorldTangent(reference, band.Lo);
        (float ex, float ez) = GridMetrics.InterpWorldTangent(reference, band.Hi);
        float rotation = (sx * ez) - (sz * ex); // 2D cross of the entry tangent with the exit tangent
        if (rotation > MinTurnRotation)
        {
            return 1f;
        }

        return rotation < -MinTurnRotation ? -1f : 0f;
    }

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
        {
            return 0f;
        }

        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2f;
    }
}
