namespace SimCoach.Coach;

/// <summary>Typical per-call token shape + per-session call volume for one cadence, used to forward-price a
/// model the user has not used yet (Screen 04). A config assumption, refined later against rolling averages.</summary>
public sealed record CadenceEstimate(int TypicalInputTokens, int TypicalOutputTokens, int CallsPerSession);

/// <summary>
/// Config inputs for <see cref="IRateCardQuery"/> forward estimates. Per-cadence token/volume assumptions plus
/// a typical lap count, so the per-lap estimate amortises a cadence's session calls over the laps.
/// </summary>
public sealed class RateCardOptions
{
    public int TypicalLapsPerSession { get; init; } = 20;

    public IReadOnlyDictionary<CoachCadence, CadenceEstimate> Cadences { get; init; } =
        new Dictionary<CoachCadence, CadenceEstimate>
        {
            [CoachCadence.Corner] = new(700, 24, 100),
            [CoachCadence.Sector] = new(900, 48, 60),
            [CoachCadence.Lap] = new(1000, 48, 20),
            [CoachCadence.Session] = new(4000, 600, 1),
        };

    public void EnsureValid()
    {
        if (TypicalLapsPerSession <= 0)
        {
            throw new InvalidOperationException("RateCardOptions.TypicalLapsPerSession must be positive.");
        }

        foreach ((CoachCadence cadence, CadenceEstimate estimate) in Cadences)
        {
            if (estimate.TypicalInputTokens < 0 || estimate.TypicalOutputTokens < 0 || estimate.CallsPerSession < 0)
            {
                throw new InvalidOperationException(
                    $"RateCardOptions estimate for '{cadence}' must have non-negative tokens and call count.");
            }
        }
    }
}
