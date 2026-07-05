namespace SimCoach.Coach.Gold;

/// <summary>
/// The corner-cadence Gold payload, derived 1:1 from a <c>CornerEvent</c>. Reference-relative scalars are
/// nullable and the builder leaves them <c>null</c> when there is no reference (the serializer then omits them,
/// rather than emitting misleading zeros). Always-present scalars (the B1 tip-quality scores, the self-only
/// trail-brake, the peak brake pressure, the bools) are non-nullable so they serialize even when <c>false</c> —
/// the registry's fail-closed clause evaluator needs them present. <see cref="PeakBrakePct"/> is the had-braking
/// gate for the reference-free <c>trail_brake_absent</c> action (0 on a flat/lift-only corner). <see cref="Reason"/>
/// is null (omitted) when empty.
/// No <c>sector_idx</c>: it is neither on the proto event nor a clause field. <see cref="CornerNameRu"/> is the
/// short Russian display form (<c>CornerNameMap.GetShort</c>) the prompt requires the model to speak; it rides
/// alongside <see cref="CornerName"/> as a separate init member so it never disturbs the positional shape.
/// </summary>
public sealed record GoldCornerEvent(
    string CornerId,
    string CornerName,
    int? DeltaMs,
    double? BrakePointDiffM,
    double? MinSpeedDiffKmh,
    double? ThrottleResumeDiffM,
    double? RacingLineDeviationM,
    double TrailBrakePctSelf,
    double PeakBrakePct,
    double? TrailBrakePctRef,
    double? TrailBrakeDiffPct,
    double UndersteerScore,
    double OversteerScore,
    double WheelspinScore,
    double BrakeOverlapSteerPct,
    double SteeringJitter,
    bool OffTrack,
    string? Reason)
{
    public string CornerNameRu { get; init; } = string.Empty;

    // M34: signed per-phase line deviation (+ = wider than the reference line, − = tighter). Nullable
    // reference-relative — null (omitted) with no reference, as separate init members so they never
    // disturb the positional shape.
    public double? EntryLineDeviationM { get; init; }

    public double? ApexLineDeviationM { get; init; }

    public double? ExitLineDeviationM { get; init; }
}
