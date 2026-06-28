using SimCoach.Coach.Actions;

namespace SimCoach.Coach;

/// <summary>
/// Coaching-engine tuning (the LLM-facing budgets, the cadence→route-key map, and the
/// <see cref="CoachPriority"/>→<see cref="CoachSeverity"/> projection bands). Mirrors
/// <c>ComputeOptions</c>: a plain options class with <c>init</c> setters and an <see cref="EnsureValid"/>
/// fail-fast — the host <c>IOptions</c> binding lands in a later PR. All thresholds live here, never as
/// magic numbers in the engine.
/// </summary>
public sealed class CoachOptions
{
    /// <summary>Max words in an in-corner tip phrase (FR-033).</summary>
    public int InCornerMaxWords { get; init; } = 8;

    /// <summary>Max words in a sector-cadence tip phrase.</summary>
    public int SectorMaxWords { get; init; } = 25;

    /// <summary>Max words in a lap-cadence tip phrase.</summary>
    public int LapMaxWords { get; init; } = 25;

    /// <summary>Max words in the session debrief phrase.</summary>
    public int DebriefMaxWords { get; init; } = 200;

    /// <summary>How many actions survive the valid-subset filter into the LLM menu.</summary>
    public int MaxActionsInMenu { get; init; } = 5;

    /// <summary>Maps each coaching cadence to the opaque LLM route key the router resolves.</summary>
    public IReadOnlyDictionary<CoachCadence, string> RouteKeys { get; init; } =
        new Dictionary<CoachCadence, string>
        {
            [CoachCadence.Corner] = "corner",
            [CoachCadence.Sector] = "sector",
            [CoachCadence.Lap] = "lap",
            [CoachCadence.Session] = "debrief",
            [CoachCadence.Strategy] = "strategy",
        };

    /// <summary>
    /// The <see cref="CoachPriority"/>→<see cref="CoachSeverity"/> projection, ordered ascending by
    /// <see cref="SeverityBand.MaxInclusive"/>; the last band is the catch-all.
    /// </summary>
    public IReadOnlyList<SeverityBand> SeverityBands { get; init; } =
    [
        new SeverityBand(new CoachPriority(CoachPhase.Entry, int.MaxValue), CoachSeverity.High),
        new SeverityBand(new CoachPriority(CoachPhase.Apex, int.MaxValue), CoachSeverity.Medium),
        new SeverityBand(new CoachPriority(CoachPhase.Exit, int.MaxValue), CoachSeverity.Low),
    ];

    /// <summary>
    /// Projects a priority to its display band: the first band (ascending) whose
    /// <see cref="SeverityBand.MaxInclusive"/> is not below <paramref name="priority"/>. Assumes the bands
    /// satisfy <see cref="EnsureValid"/> (covering catch-all last).
    /// </summary>
    public CoachSeverity SeverityFor(CoachPriority priority)
    {
        foreach (SeverityBand band in SeverityBands)
        {
            if (priority <= band.MaxInclusive)
            {
                return band.Band;
            }
        }

        return SeverityBands[^1].Band;
    }

    public void EnsureValid()
    {
        if (InCornerMaxWords <= 0 || SectorMaxWords <= 0 || LapMaxWords <= 0 || DebriefMaxWords <= 0)
        {
            throw new InvalidOperationException("CoachOptions word budgets must all be positive.");
        }

        if (MaxActionsInMenu <= 0)
        {
            throw new InvalidOperationException("CoachOptions.MaxActionsInMenu must be positive.");
        }

        foreach (CoachCadence cadence in Enum.GetValues<CoachCadence>())
        {
            if (!RouteKeys.TryGetValue(cadence, out string? routeKey) || string.IsNullOrWhiteSpace(routeKey))
            {
                throw new InvalidOperationException($"CoachOptions.RouteKeys is missing cadence '{cadence}'.");
            }
        }

        if (SeverityBands.Count == 0)
        {
            throw new InvalidOperationException("CoachOptions.SeverityBands must not be empty.");
        }

        for (int i = 1; i < SeverityBands.Count; i++)
        {
            if (SeverityBands[i].MaxInclusive <= SeverityBands[i - 1].MaxInclusive)
            {
                throw new InvalidOperationException(
                    "CoachOptions.SeverityBands must be strictly ascending by MaxInclusive.");
            }
        }

        CoachPhase topPhase = Enum.GetValues<CoachPhase>()[^1];
        CoachPriority maxPriority = new(topPhase, int.MaxValue);
        if (SeverityBands[^1].MaxInclusive < maxPriority)
        {
            throw new InvalidOperationException(
                "CoachOptions.SeverityBands must cover the full priority range (top band is the catch-all).");
        }
    }
}
