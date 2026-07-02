namespace SimCoach.RuEval;

/// <summary>
/// Pure score math: folds one verdict into a composite via the rubric weights, and reduces the
/// <c>SampleCount</c> verdicts for a fixture into a single <see cref="EvalOutcome"/> (average each dimension,
/// then apply the bar and the hard groundedness floor). No network, no I/O — the always-on hermetic self-tests
/// exercise exactly this.
/// </summary>
public static class ScoreAggregator
{
    /// <summary>Weighted composite of one verdict on the same 0..MaxDimensionScore scale as the inputs.</summary>
    public static double Composite(JudgeVerdict verdict, RubricWeights weights)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        ArgumentNullException.ThrowIfNull(weights);
        return (verdict.Groundedness * weights.Groundedness)
            + (verdict.Brevity * weights.Brevity)
            + (verdict.NaturalRussian * weights.NaturalRussian)
            + (verdict.Actionability * weights.Actionability)
            + (verdict.Tone * weights.Tone);
    }

    /// <summary>
    /// Averages the samples' dimensions, computes the composite from the averaged groundedness/etc., and
    /// evaluates all three gate legs against <see cref="RuEvalOptions.PassBar"/>,
    /// <see cref="RuEvalOptions.GroundednessFloor"/>, and the per-dimension
    /// <see cref="RuEvalOptions.MinDimensionScore"/> floor (applied to every averaged dimension).
    /// </summary>
    public static EvalOutcome Evaluate(IReadOnlyList<JudgeVerdict> samples, RuEvalOptions options)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(options);
        if (samples.Count == 0)
        {
            throw new InvalidOperationException("ScoreAggregator.Evaluate requires at least one verdict.");
        }

        double avgGroundedness = samples.Average(v => (double)v.Groundedness);
        double composite = samples.Average(v => Composite(v, options.Weights));

        double floor = options.MinDimensionScore;
        bool dimensionFloorsCleared =
            avgGroundedness >= floor
            && samples.Average(v => (double)v.Brevity) >= floor
            && samples.Average(v => (double)v.NaturalRussian) >= floor
            && samples.Average(v => (double)v.Actionability) >= floor
            && samples.Average(v => (double)v.Tone) >= floor;

        bool barCleared = composite >= options.PassBar;
        bool floorCleared = avgGroundedness >= options.GroundednessFloor;
        return new EvalOutcome(composite, avgGroundedness, barCleared, floorCleared, dimensionFloorsCleared);
    }
}
