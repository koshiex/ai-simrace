namespace SimCoach.RuEval;

/// <summary>
/// Pure reducer for the A/B shadow-harness (M30): folds the flat per-(candidate, fixture)
/// <see cref="AbFixtureSample"/> measurements into one ranked <see cref="AbCandidateOutcome"/> per candidate
/// model. Ranking is quality-first (mean composite descending) with a cost tiebreak (total ledger cost
/// ascending), so an equal-quality but cheaper model sorts ahead. No network, no I/O — the always-on hermetic
/// self-tests exercise exactly this, and the network <c>[Fact]</c> only feeds it real measurements.
/// </summary>
public static class AbScorecard
{
    public static IReadOnlyList<AbCandidateOutcome> Rank(IReadOnlyList<AbFixtureSample> samples, RubricWeights weights)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(weights);

        return [.. samples
            .GroupBy(s => (s.RouteKey, s.ModelId))
            .Select(group => Reduce(group.Key.RouteKey, group.Key.ModelId, [.. group], weights))
            .OrderByDescending(outcome => outcome.Composite)
            .ThenBy(outcome => outcome.TotalCostUsd)];
    }

    private static AbCandidateOutcome Reduce(
        string routeKey, string modelId, IReadOnlyList<AbFixtureSample> samples, RubricWeights weights)
    {
        IReadOnlyList<AbFixtureSample> judged = [.. samples.Where(s => s.FormatOk && s.Verdicts.Count > 0)];
        int formatRejects = samples.Count - judged.Count;

        double composite = MeanOverJudged(judged, s => FixtureComposite(s, weights));
        double groundedness = MeanOverJudged(judged, s => s.Verdicts.Average(v => (double)v.Groundedness));
        double brevity = MeanOverJudged(judged, s => s.Verdicts.Average(v => (double)v.Brevity));
        double naturalRussian = MeanOverJudged(judged, s => s.Verdicts.Average(v => (double)v.NaturalRussian));
        double actionability = MeanOverJudged(judged, s => s.Verdicts.Average(v => (double)v.Actionability));
        double tone = MeanOverJudged(judged, s => s.Verdicts.Average(v => (double)v.Tone));

        double totalCost = samples.Sum(s => s.CostUsd);
        double avgLatency = samples.Count == 0 ? 0d : samples.Average(s => s.LatencyMs);

        return new AbCandidateOutcome(
            routeKey, modelId, composite, groundedness, brevity, naturalRussian, actionability, tone,
            totalCost, avgLatency, judged.Count, formatRejects);
    }

    private static double FixtureComposite(AbFixtureSample sample, RubricWeights weights)
        => sample.Verdicts.Average(v => ScoreAggregator.Composite(v, weights));

    private static double MeanOverJudged(IReadOnlyList<AbFixtureSample> judged, Func<AbFixtureSample, double> selector)
        => judged.Count == 0 ? 0d : judged.Average(selector);
}
