namespace SimCoach.Coach;

/// <summary>
/// Forward cost estimate for a model the user is about to switch to (Screen 04: "~$0.002 / круг",
/// "~$0.01 / сессия"). Historical spend cannot price a never-used model, so this prices from the config rate
/// card × per-cadence typical-token assumptions. Lives in Coach: it couples LLM rates with per-cadence token
/// budgets, and only Coach sits above both assemblies.
/// </summary>
public interface IRateCardQuery
{
    Task<decimal> EstimatePerLapUsd(string modelId, CoachCadence cadence, CancellationToken ct);

    Task<decimal> EstimatePerSessionUsd(string modelId, CancellationToken ct);
}
