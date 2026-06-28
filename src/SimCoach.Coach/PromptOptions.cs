namespace SimCoach.Coach;

/// <summary>
/// Selects the versioned system + few-shot prompt resources per cadence (M4). Mirrors
/// <c>CoachOptions</c>/<c>ComputeOptions</c>: <c>init</c> setters + an <see cref="EnsureValid"/> fail-fast.
/// Locale is intentionally RU-fixed for the MVP (the Gold <c>locale</c> is not consulted); multi-locale is
/// out of scope. <see cref="CoachCadence.Strategy"/> is reserved (no MVP tip), so it carries no selection.
/// </summary>
public sealed class PromptOptions
{
    /// <summary>The real cadences a prompt is ever built for (Strategy is reserved and excluded).</summary>
    public static readonly IReadOnlyList<CoachCadence> RealCadences =
        [CoachCadence.Corner, CoachCadence.Sector, CoachCadence.Lap, CoachCadence.Session];

    /// <summary>Per-cadence resource-version selection. Strategy is intentionally absent.</summary>
    public IReadOnlyDictionary<CoachCadence, PromptSelection> Cadences { get; init; } =
        new Dictionary<CoachCadence, PromptSelection>
        {
            [CoachCadence.Corner] = new PromptSelection(),
            [CoachCadence.Sector] = new PromptSelection(),
            [CoachCadence.Lap] = new PromptSelection(),
            [CoachCadence.Session] = new PromptSelection(),
        };

    /// <summary>Resolves the selection for a cadence, throwing a clear error for an unsupported one.</summary>
    public PromptSelection For(CoachCadence cadence)
    {
        if (!Cadences.TryGetValue(cadence, out PromptSelection? selection))
        {
            throw new InvalidOperationException($"PromptOptions has no prompt selection for cadence '{cadence}'.");
        }

        return selection;
    }

    public void EnsureValid()
    {
        foreach (CoachCadence cadence in RealCadences)
        {
            if (!Cadences.TryGetValue(cadence, out PromptSelection? selection))
            {
                throw new InvalidOperationException($"PromptOptions.Cadences is missing cadence '{cadence}'.");
            }

            if (string.IsNullOrWhiteSpace(selection.SystemVersion))
            {
                throw new InvalidOperationException(
                    $"PromptOptions.Cadences['{cadence}'].SystemVersion must not be empty.");
            }

            if (string.IsNullOrWhiteSpace(selection.FewShotVersion))
            {
                throw new InvalidOperationException(
                    $"PromptOptions.Cadences['{cadence}'].FewShotVersion must not be empty.");
            }
        }
    }
}

/// <summary>
/// The versioned prompt selection for one cadence. <see cref="OverridePath"/>, when set, replaces the
/// embedded system prompt with an on-disk file (few-shots always come from the embedded resource).
/// </summary>
public sealed record PromptSelection(
    string SystemVersion = "v1",
    string FewShotVersion = "v1",
    string? OverridePath = null);
