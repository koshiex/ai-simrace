namespace SimCoach.RuEval;

/// <summary>
/// The five-dimension rubric weights (M18-rubric decision) used to fold per-dimension judge scores into one
/// composite. Groundedness is weighted heaviest and additionally guarded by a HARD floor in
/// <see cref="RuEvalOptions"/> — the weights alone can never let a fluent-but-ungrounded phrase pass. Tier-2
/// dev-config (not a user slider); the concrete numbers are calibration knobs, not magic constants.
/// </summary>
public sealed record RubricWeights
{
    /// <summary>Does the phrase only assert facts present in the Gold artifact? (0..1 share of the composite.)</summary>
    public double Groundedness { get; init; } = 0.35;

    /// <summary>One short imperative, no rambling — the voice budget. (0..1 share of the composite.)</summary>
    public double Brevity { get; init; } = 0.15;

    /// <summary>Idiomatic spoken Russian, no transliteration/machine phrasing. (0..1 share of the composite.)</summary>
    public double NaturalRussian { get; init; } = 0.15;

    /// <summary>A concrete driving action the driver can apply next lap. (0..1 share of the composite.)</summary>
    public double Actionability { get; init; } = 0.20;

    /// <summary>Coach-appropriate tone — no raw numbers read aloud, no blame. (0..1 share of the composite.)</summary>
    public double Tone { get; init; } = 0.15;

    public double Sum => Groundedness + Brevity + NaturalRussian + Actionability + Tone;

    public void EnsureValid()
    {
        foreach ((string name, double weight) in Enumerate())
        {
            if (weight is < 0d or > 1d)
            {
                throw new InvalidOperationException($"RubricWeights.{name} must be within [0, 1] (was {weight}).");
            }
        }

        if (Math.Abs(Sum - 1d) > 0.001d)
        {
            throw new InvalidOperationException($"RubricWeights must sum to 1.0 (was {Sum}).");
        }
    }

    private IEnumerable<(string Name, double Weight)> Enumerate()
    {
        yield return (nameof(Groundedness), Groundedness);
        yield return (nameof(Brevity), Brevity);
        yield return (nameof(NaturalRussian), NaturalRussian);
        yield return (nameof(Actionability), Actionability);
        yield return (nameof(Tone), Tone);
    }
}
