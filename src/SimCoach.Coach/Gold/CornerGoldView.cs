using SimCoach.Coach.Actions;

namespace SimCoach.Coach.Gold;

/// <summary>
/// The typed <see cref="IGoldView"/> over a corner Gold artifact — the production replacement for
/// <see cref="DictionaryGoldView"/>. The field switch matches <c>GoldFieldNames.For(Corner)</c> exactly; a
/// dropped (null) reference-relative field returns <c>false</c>, mirroring the serialized omission.
/// </summary>
public sealed class CornerGoldView : IGoldView
{
    private readonly GoldArtifact<GoldCornerEvent> _artifact;

    public CornerGoldView(GoldArtifact<GoldCornerEvent> artifact) => _artifact = artifact;

    public CoachCadence Cadence => CoachCadence.Corner;

    public bool HasReference => _artifact.Session.HasReference;

    public bool TryGetNumber(string field, out double value)
    {
        GoldCornerEvent e = _artifact.Event;
        switch (field)
        {
            case "delta_ms": return GoldScalar.Num(e.DeltaMs, out value);
            case "brake_point_diff_m": return GoldScalar.Num(e.BrakePointDiffM, out value);
            case "min_speed_diff_kmh": return GoldScalar.Num(e.MinSpeedDiffKmh, out value);
            case "throttle_resume_diff_m": return GoldScalar.Num(e.ThrottleResumeDiffM, out value);
            case "racing_line_deviation_m": return GoldScalar.Num(e.RacingLineDeviationM, out value);
            case "entry_line_deviation_m": return GoldScalar.Num(e.EntryLineDeviationM, out value);
            case "apex_line_deviation_m": return GoldScalar.Num(e.ApexLineDeviationM, out value);
            case "exit_line_deviation_m": return GoldScalar.Num(e.ExitLineDeviationM, out value);
            case "brake_release_diff_m": return GoldScalar.Num(e.BrakeReleaseDiffM, out value);
            case "trail_brake_pct_self": return GoldScalar.Num(e.TrailBrakePctSelf, out value);
            case "peak_brake_pct": return GoldScalar.Num(e.PeakBrakePct, out value);
            case "trail_brake_pct_ref": return GoldScalar.Num(e.TrailBrakePctRef, out value);
            case "trail_brake_diff_pct": return GoldScalar.Num(e.TrailBrakeDiffPct, out value);
            case "understeer_score": return GoldScalar.Num(e.UndersteerScore, out value);
            case "oversteer_score": return GoldScalar.Num(e.OversteerScore, out value);
            case "wheelspin_score": return GoldScalar.Num(e.WheelspinScore, out value);
            case "brake_lockup_score": return GoldScalar.Num(e.BrakeLockupScore, out value);
            case "short_shift_score": return GoldScalar.Num(e.ShortShiftScore, out value);
            case "brake_overlap_steer_pct": return GoldScalar.Num(e.BrakeOverlapSteerPct, out value);
            case "steering_jitter": return GoldScalar.Num(e.SteeringJitter, out value);
            default: value = 0d; return false;
        }
    }

    public bool TryGetBool(string field, out bool value)
    {
        switch (field)
        {
            case "off_track": value = _artifact.Event.OffTrack; return true;
            case "has_reference": value = _artifact.Session.HasReference; return true;
            default: value = false; return false;
        }
    }

    public bool TryGetString(string field, out string value)
    {
        GoldCornerEvent e = _artifact.Event;
        switch (field)
        {
            case "corner_id": return GoldScalar.Str(e.CornerId, out value);
            case "corner_name": return GoldScalar.Str(e.CornerName, out value);
            case "reason": return GoldScalar.Str(e.Reason, out value);
            default: value = string.Empty; return false;
        }
    }
}
