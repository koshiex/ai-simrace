using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;
using SimCoach.Storage;

namespace SimCoach.Reference;

/// <summary>
/// What a corner contributed to the lap, for <c>top_losses</c> aggregation. <see cref="ApexPosition"/>
/// places it in a sector; <see cref="DeltaMs"/> is the time lost vs the reference (0 when no reference,
/// so it never appears in a losses list).
/// </summary>
internal sealed record CornerContribution(
    string CornerId, int DeltaMs, float ApexPosition, string Reason, float UndersteerScore, float OversteerScore);

/// <summary>
/// Builds a <see cref="CornerEvent"/> from a self corner window and (optionally) the reference grid.
/// Self-derived fields (trail-brake, balance, off-track) are always populated; reference-relative
/// fields (deltas, diffs, racing-line) are populated only when a reference exists, otherwise left at
/// proto defaults (0). Signs follow <c>telemetry.proto</c>: positive delta = slower, negative diff =
/// earlier/slower/later as documented per field.
/// </summary>
internal static class CornerEventBuilder
{
    private const float MetresPerSecondToKmh = 3.6f;

    public static (CornerEvent Event, CornerContribution Contribution) Build(
        Corner corner,
        IReadOnlyList<TelemetryFrame> selfFrames,
        ResampledLap? reference,
        float lapLengthM,
        int gridLength)
    {
        BrakeProfile brakeSelf = BrakeKernels.Analyze(selfFrames);
        CornerMetrics speedSelf = ThrottleSpeedKernels.Analyze(selfFrames);
        BalanceScores balanceSelf = BalanceKernels.Analyze(selfFrames);
        bool offTrack = OffTrack(selfFrames);

        var ev = new CornerEvent
        {
            T = selfFrames[^1].T,
            CornerId = corner.Id,
            TrailBrakePctSelf = brakeSelf.TrailBrakePct,
            UndersteerScore = balanceSelf.UndersteerScore,
            OversteerScore = balanceSelf.OversteerScore,
            OffTrack = offTrack,
            WheelspinScore = WheelspinKernels.WheelspinScore(selfFrames),
            BrakeOverlapSteerPct = BrakeOverlapSteerKernels.OverlapPct(selfFrames),
            SteeringJitter = SteeringJitterKernels.SteeringJitter(selfFrames),
        };

        bool hasReference = reference is not null && lapLengthM > 0f;
        if (!hasReference)
        {
            string selfReason = offTrack ? "off_track" : string.Empty;
            ev.Reason = selfReason;
            return (ev, new CornerContribution(
                corner.Id, 0, speedSelf.MinSpeedPosition, selfReason,
                balanceSelf.UndersteerScore, balanceSelf.OversteerScore));
        }

        ResampledLap refLap = reference!;
        int k0 = GridMetrics.Index(corner.StartPosition, gridLength);
        int k1 = GridMetrics.Index(corner.EndPosition, gridLength);
        IReadOnlyList<TelemetryFrame> refFrames = GridMetrics.SliceToFrames(refLap, k0, k1);
        if (refFrames.Count == 0 || k1 <= k0)
        {
            // Degenerate mapping — fall back to self-only.
            string selfReason = offTrack ? "off_track" : string.Empty;
            ev.Reason = selfReason;
            return (ev, new CornerContribution(
                corner.Id, 0, speedSelf.MinSpeedPosition, selfReason,
                balanceSelf.UndersteerScore, balanceSelf.OversteerScore));
        }

        BrakeProfile brakeRef = BrakeKernels.Analyze(refFrames);
        CornerMetrics speedRef = ThrottleSpeedKernels.Analyze(refFrames);

        int selfDurationMs = DurationMs(selfFrames);
        int refDurationMs = refLap.TMsFromLapStart[k1] - refLap.TMsFromLapStart[k0];
        int deltaMs = selfDurationMs - refDurationMs;

        float brakePointDiffM =
            ((brakeSelf.BrakeOnPosition ?? corner.StartPosition) - (brakeRef.BrakeOnPosition ?? corner.StartPosition))
            * lapLengthM;
        float minSpeedDiffKmh = (speedSelf.MinSpeedMps - speedRef.MinSpeedMps) * MetresPerSecondToKmh;
        float throttleResumeDiffM =
            ((speedRef.ThrottleOnPosition ?? corner.EndPosition) - (speedSelf.ThrottleOnPosition ?? corner.EndPosition))
            * lapLengthM;
        float racingLineDeviationM = RacingLineDeviation(selfFrames, refLap);

        ev.DeltaMs = deltaMs;
        ev.BrakePointDiffM = brakePointDiffM;
        ev.MinSpeedDiffKmh = minSpeedDiffKmh;
        ev.ThrottleResumeDiffM = throttleResumeDiffM;
        ev.TrailBrakePctRef = brakeRef.TrailBrakePct;
        ev.RacingLineDeviationM = racingLineDeviationM;

        string reason = ChooseReason(offTrack, throttleResumeDiffM, brakePointDiffM, minSpeedDiffKmh);
        ev.Reason = reason;
        return (ev, new CornerContribution(
            corner.Id, deltaMs, speedSelf.MinSpeedPosition, reason,
            balanceSelf.UndersteerScore, balanceSelf.OversteerScore));
    }

    private static bool OffTrack(IReadOnlyList<TelemetryFrame> frames)
    {
        foreach (TelemetryFrame frame in frames)
        {
            if (frame.TyresOut > 0 || !frame.IsValidLap)
            {
                return true;
            }
        }

        return false;
    }

    private static int DurationMs(IReadOnlyList<TelemetryFrame> frames)
    {
        var start = frames[0].T.ToDateTimeOffset();
        var end = frames[^1].T.ToDateTimeOffset();
        return (int)(end - start).TotalMilliseconds;
    }

    private static float RacingLineDeviation(IReadOnlyList<TelemetryFrame> selfFrames, ResampledLap reference)
    {
        double sumSquares = 0;
        int count = 0;
        foreach (TelemetryFrame frame in selfFrames)
        {
            (float refX, float refZ) = GridMetrics.InterpWorldXZ(reference, frame.NormalizedCarPosition);
            float selfX = frame.WorldPos?.X ?? 0f;
            float selfZ = frame.WorldPos?.Z ?? 0f;
            double dx = selfX - refX;
            double dz = selfZ - refZ;
            sumSquares += (dx * dx) + (dz * dz);
            count++;
        }

        return count > 0 ? (float)Math.Sqrt(sumSquares / count) : 0f;
    }

    /// <summary>
    /// The dominant time-loss cause — a deliberately rough heuristic for the <b>advisory</b>
    /// <c>CornerLoss.reason</c> label only (a Phase-3 prompt hint). It compares magnitudes across mixed
    /// units (metres for brake/throttle, km/h for speed), so the label is approximate; the authoritative
    /// loss magnitude is <c>delta_ms</c>, computed separately and unit-correct. Off-track wins outright.
    /// </summary>
    private static string ChooseReason(bool offTrack, float throttleResumeDiffM, float brakePointDiffM, float minSpeedDiffKmh)
    {
        if (offTrack)
        {
            return "off_track";
        }

        float lateThrottle = throttleResumeDiffM < 0f ? -throttleResumeDiffM : 0f; // self resumed later (m)
        float earlyBrake = brakePointDiffM < 0f ? -brakePointDiffM : 0f;           // self braked earlier (m)
        float lowMinSpeed = minSpeedDiffKmh < 0f ? -minSpeedDiffKmh : 0f;          // self slower min speed (km/h)

        float max = MathF.Max(lateThrottle, MathF.Max(earlyBrake, lowMinSpeed));
        if (max <= 0f)
        {
            return "slower";
        }

        if (max == lateThrottle)
        {
            return "late_throttle";
        }

        return max == earlyBrake ? "early_brake" : "low_min_speed";
    }
}
