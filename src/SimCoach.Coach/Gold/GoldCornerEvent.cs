namespace SimCoach.Coach.Gold;

/// <summary>
/// The corner-cadence Gold payload, derived 1:1 from a <c>CornerEvent</c>. Reference-relative scalars are
/// nullable and the builder leaves them <c>null</c> when there is no reference (the serializer then omits them,
/// rather than emitting misleading zeros). Always-present scalars (the B1 tip-quality scores, the self-only
/// trail-brake, the bools) are non-nullable so they serialize even when <c>false</c> — the registry's
/// fail-closed clause evaluator needs them present. <see cref="Reason"/> is null (omitted) when empty.
/// No <c>sector_idx</c>: it is neither on the proto event nor a clause field.
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
    double? TrailBrakePctRef,
    double? TrailBrakeDiffPct,
    double UndersteerScore,
    double OversteerScore,
    double WheelspinScore,
    double BrakeOverlapSteerPct,
    double SteeringJitter,
    bool OffTrack,
    string? Reason);
