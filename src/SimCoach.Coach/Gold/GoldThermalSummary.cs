namespace SimCoach.Coach.Gold;

/// <summary>
/// The lap-cadence tyre/brake-temp abuse summary (B1). The two overheat bools are always populated (the builder
/// substitutes a zeroed summary for an absent proto <c>thermal</c> message) so the fail-closed clause evaluator
/// can read <c>tyre_overheat</c>/<c>brake_overheat</c>.
/// </summary>
public sealed record GoldThermalSummary(
    double MaxTyreTempC,
    double MaxBrakeTempC,
    bool TyreOverheat,
    bool BrakeOverheat);
