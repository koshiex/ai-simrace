using System.Collections.Frozen;

namespace SimCoach.Coach.Actions;

/// <summary>
/// The checked-in, per-cadence set of <b>scalar</b> Gold field names an action's <c>when</c>/<c>param</c> may
/// reference. It is the registry loader's fail-fast source (an unknown field throws at load) and the flat
/// projection a later PR's <see cref="IGoldView"/> adapter must expose. Non-scalar payload (e.g.
/// <c>top_losses</c>, a repeated message) is intentionally excluded — the evaluator/renderer handle scalars
/// only. A later PR re-validates the fields actions actually use against the real Gold records (same source).
/// </summary>
public static class GoldFieldNames
{
    private static readonly FrozenSet<string> _corner = new[]
    {
        "corner_id", "corner_name", "delta_ms", "brake_point_diff_m", "min_speed_diff_kmh",
        "throttle_resume_diff_m", "racing_line_deviation_m", "trail_brake_pct_self", "peak_brake_pct",
        "trail_brake_pct_ref", "trail_brake_diff_pct", "understeer_score", "oversteer_score", "wheelspin_score",
        "brake_overlap_steer_pct", "steering_jitter", "off_track", "reason", "has_reference",
        "entry_line_deviation_m", "apex_line_deviation_m", "exit_line_deviation_m", "brake_release_diff_m",
        "brake_lockup_score",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> _sector = new[]
    {
        "sector_idx", "delta_ms", "top_corner", "has_reference",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> _lap = new[]
    {
        "lap_number", "delta_ms", "is_pb", "is_clean", "top_corner", "max_tyre_temp_c", "max_brake_temp_c",
        "tyre_overheat", "brake_overheat", "has_reference",
    }.ToFrozenSet(StringComparer.Ordinal);

    // The flat scalar surface of GoldSessionPayload (plus has_reference off the session header). Non-scalar
    // aggregates (aggregated_losses, sector_avg_delta_ms, fuel_tyre, stints) are intentionally excluded — the
    // evaluator/renderer handle scalars only. M20 groundwork for the open Session-Gold-view question; there are
    // no Session registry actions yet, so this only guards catalog/record drift (GoldFieldNamesTests).
    private static readonly FrozenSet<string> _session = new[]
    {
        "lap_count", "clean_lap_count", "pb_time_ms", "average_lap_ms", "understeer_trend",
        "consistency_stddev_ms", "theoretical_best_gap_ms", "optimal_gap_ms", "has_reference",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The scalar field-name set for a cadence. Throws for <see cref="CoachCadence.Strategy"/>, which has no Gold
    /// payload in the MVP (an empty set would only surface later as a confusing "unknown field" error).
    /// </summary>
    public static IReadOnlySet<string> For(CoachCadence cadence) => cadence switch
    {
        CoachCadence.Corner => _corner,
        CoachCadence.Sector => _sector,
        CoachCadence.Lap => _lap,
        CoachCadence.Session => _session,
        _ => throw new NotSupportedException($"No Gold field-name set is defined for cadence '{cadence}'."),
    };
}
