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
        int gridLength,
        float brakeWindowUpstreamM,
        double apexWindowFraction)
    {
        // M2: every self-derived kernel and the self duration are measured over the geometric
        // [StartPosition, EndPosition] sub-window — the same span the reference grid slice covers —
        // never over the raw tracker buffer (which M16 later extends upstream of the start).
        List<TelemetryFrame> selfInSpan = FramesInSpan(selfFrames, corner.StartPosition, corner.EndPosition);
        // A degenerate window (no buffered frame landed inside [Start,End]) falls back to the raw buffer
        // ONLY for the always-populated balance/off-track/speed kernels, which need a non-empty input.
        // selfDurationMs/deltaMs must NEVER ride that widened buffer — the self-side degenerate guard
        // below takes a self-only return (deltaMs=0) instead of measuring the upstream travel time.
        bool selfSpanDegenerate = selfInSpan.Count == 0;
        IReadOnlyList<TelemetryFrame> selfSpan = selfSpanDegenerate ? selfFrames : selfInSpan;

        BrakeProfile brakeSelf = BrakeKernels.Analyze(selfSpan);
        CornerMetrics speedSelf = ThrottleSpeedKernels.Analyze(selfSpan);
        BalanceScores balanceSelf = BalanceKernels.Analyze(selfSpan);
        bool offTrack = OffTrack(selfSpan);

        // M9: unwanted brake-while-steering is measured ONLY over the turn-in → apex band, not the whole
        // geometric window — so a straight-line braking approach or a full braking chicane no longer
        // inflates the fraction and mis-fires "выпрямляй руль". The band comes from the SAME shared
        // apex-band helper the live corner-phase gate uses (one definition of "apex"). An empty band ⇒ 0
        // (kernel contract). The band is a sub-range of [Start,End], so no new S/F wrap surface is added.
        (float bandLo, float bandHi) = CornerPhaseBands.TurnInToApexBand(
            corner.StartPosition, corner.ApexPosition, corner.EndPosition, apexWindowFraction);
        IReadOnlyList<TelemetryFrame> overlapSpan = FramesInSpan(selfSpan, bandLo, bandHi);

        var ev = new CornerEvent
        {
            T = selfFrames[^1].T,
            CornerId = corner.Id,
            TrailBrakePctSelf = brakeSelf.TrailBrakePct,
            PeakBrakePct = brakeSelf.PeakBrakePct,
            UndersteerScore = balanceSelf.UndersteerScore,
            OversteerScore = balanceSelf.OversteerScore,
            OffTrack = offTrack,
            WheelspinScore = WheelspinKernels.WheelspinScore(selfSpan),
            BrakeOverlapSteerPct = BrakeOverlapSteerKernels.OverlapPct(overlapSpan),
            SteeringJitter = SteeringJitterKernels.SteeringJitter(selfSpan),
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
        if (refFrames.Count == 0 || k1 <= k0 || selfSpanDegenerate)
        {
            // Degenerate mapping OR an empty self in-span window — fall back to self-only (deltaMs=0). This
            // is the mirror of the reference degenerate branch: measuring the self duration over the
            // M16-widened buffer would inflate delta by the upstream travel time (an M2 span mismatch).
            string selfReason = offTrack ? "off_track" : string.Empty;
            ev.Reason = selfReason;
            return (ev, new CornerContribution(
                corner.Id, 0, speedSelf.MinSpeedPosition, selfReason,
                balanceSelf.UndersteerScore, balanceSelf.OversteerScore));
        }

        BrakeProfile brakeRef = BrakeKernels.Analyze(refFrames);
        CornerMetrics speedRef = ThrottleSpeedKernels.Analyze(refFrames);

        int selfDurationMs = DurationMs(selfSpan);
        int refDurationMs = refLap.TMsFromLapStart[k1] - refLap.TMsFromLapStart[k0];
        int deltaMs = selfDurationMs - refDurationMs;

        // M16: brake onset is the ONE metric read over the upstream-widened pre-roll (the real braking
        // zone starts before the geometric corner). Both sides widen by the same metric distance so a
        // symmetric extension cannot bias the diff. Everything else above stays on the [Start,End]
        // sub-window (M2's contract); the widened slices are local to BrakeOnPosition and go nowhere else.
        float upstreamNormalized = brakeWindowUpstreamM / lapLengthM;
        // Brake-onset scan reads the upstream-widened slice; here the raw-buffer fallback is safe because
        // this feeds only BrakeOnPosition, never the [Start,End]-bound delta/min-speed kernels.
        List<TelemetryFrame> selfBrakeInSpan =
            FramesInSpan(selfFrames, corner.StartPosition - upstreamNormalized, corner.EndPosition);
        IReadOnlyList<TelemetryFrame> selfBrakeScan = selfBrakeInSpan.Count > 0 ? selfBrakeInSpan : selfFrames;
        int upstreamGrid = (int)MathF.Round(upstreamNormalized * gridLength);
        int k0Brake = Math.Max(0, k0 - upstreamGrid);
        IReadOnlyList<TelemetryFrame> refBrakeScan = GridMetrics.SliceToFrames(refLap, k0Brake, k1);
        float? selfBrakeOn = BrakeKernels.Analyze(selfBrakeScan).BrakeOnPosition;
        float? refBrakeOn = BrakeKernels.Analyze(refBrakeScan).BrakeOnPosition;

        float brakePointDiffM =
            ((selfBrakeOn ?? corner.StartPosition) - (refBrakeOn ?? corner.StartPosition))
            * lapLengthM;

        // D-minspeed: suppress the min-speed contribution for a corner with no true in-span minimum
        // (flat/transit corners), so it stays silent on min-speed instead of emitting boundary noise.
        float minSpeedDiffKmh = speedSelf.HasInSpanMinimum
            ? (speedSelf.MinSpeedMps - speedRef.MinSpeedMps) * MetresPerSecondToKmh
            : 0f;
        float throttleResumeDiffM =
            ((speedRef.ThrottleOnPosition ?? corner.EndPosition) - (speedSelf.ThrottleOnPosition ?? corner.EndPosition))
            * lapLengthM;
        float racingLineDeviationM = RacingLineDeviation(selfSpan, refLap);

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

    /// <summary>
    /// The frames of the single lap-crossing window that fall inside <c>[start, end]</c>. Frames the
    /// tracker buffered upstream of the start (M16) or the one frame past the end are excluded so every
    /// self kernel can see exactly the reference span. Returns the raw filter result (possibly empty);
    /// the caller decides whether a degenerate empty window falls back to the raw buffer (kernels needing
    /// a non-empty input) or takes a self-only return (the duration/delta path, which must never ride the
    /// widened buffer).
    /// </summary>
    private static List<TelemetryFrame> FramesInSpan(
        IReadOnlyList<TelemetryFrame> frames, float start, float end)
    {
        List<TelemetryFrame> span = [];
        foreach (TelemetryFrame frame in frames)
        {
            float pos = frame.NormalizedCarPosition;
            if (pos >= start && pos <= end)
            {
                span.Add(frame);
            }
        }

        return span;
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
            // Skip frames with no world position: null, or the (0,0,0) honest-zero sentinel the ACC mapper
            // writes when the player slot is out of range (AccFrameMapper's `new Vec3()`). Folding a (0,0)
            // origin into the RMS would inflate racing_line_deviation_m by the car's full distance from the
            // track origin — a phantom line error on torn/slot-less frames.
            Vec3? worldPos = frame.WorldPos;
            if (worldPos is null || (worldPos.X == 0f && worldPos.Y == 0f && worldPos.Z == 0f))
            {
                continue;
            }

            (float refX, float refZ) = GridMetrics.InterpWorldXZ(reference, frame.NormalizedCarPosition);
            double dx = worldPos.X - refX;
            double dz = worldPos.Z - refZ;
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
