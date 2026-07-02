namespace SimCoach.RuEval;

/// <summary>
/// The RU-eval gate's config surface (Tier-2 dev-config, NOT a user slider): the rubric weights, the numeric
/// pass bar, the HARD groundedness floor, the judge route key, the per-fixture sample count, and the
/// gate-vs-advisory switch. Every threshold is here — no magic numbers in the harness. The owner-decided
/// stance is fixed (judge = <c>anthropic/claude-sonnet-4.6</c>, reference-anchored, 5-dim rubric + hard
/// groundedness floor, hard-fail known-bad anchors, good-fixture bar release-blocking only after calibration);
/// the concrete numbers below are the calibration knobs.
/// </summary>
public sealed record RuEvalOptions
{
    /// <summary>Per-dimension score ceiling the judge is asked to use (each dimension is scored 0..this).</summary>
    public int MaxDimensionScore { get; init; } = 5;

    /// <summary>Rubric dimension weights folded into the composite (see <see cref="RubricWeights"/>).</summary>
    public RubricWeights Weights { get; init; } = new();

    /// <summary>Composite (on the 0..<see cref="MaxDimensionScore"/> scale) a good phrase must clear to pass.</summary>
    public double PassBar { get; init; } = 3.5d;

    /// <summary>
    /// The HARD groundedness floor (M18-rubric decision): a phrase whose averaged groundedness is below this
    /// can never pass, whatever its composite. Fluent-but-ungrounded output is failed outright.
    /// </summary>
    public double GroundednessFloor { get; init; } = 3.0d;

    /// <summary>
    /// The per-dimension severe-violation floor: a phrase whose averaged score on ANY rubric dimension is below
    /// this can never pass, whatever its composite. This is what makes the known-bad anchors deterministically
    /// fail: an anchor bad in only ONE dimension (a transliteration → natural-Russian; a raw-number → tone) has a
    /// weighted composite of <c>MaxDimensionScore * (1 - dimWeight)</c> that can still clear <see cref="PassBar"/>
    /// (5 * (1 - 0.15) = 4.25 &gt; 3.5), so the composite alone cannot catch it — a single severe violation must.
    /// Kept below <see cref="GroundednessFloor"/> so the groundedness floor stays the stricter, dedicated leg.
    /// Valid range: [0, <see cref="MaxDimensionScore"/>].
    /// </summary>
    public double MinDimensionScore { get; init; } = 2.0d;

    /// <summary>The route key the judge call resolves through (added to the eval's appsettings only).</summary>
    public string JudgeRouteKey { get; init; } = "ru_judge";

    /// <summary>How many judge calls to average per fixture to damp nondeterminism (≥ 1).</summary>
    public int SampleCount { get; init; } = 1;

    /// <summary>
    /// Gate-vs-advisory (M18-gate decision). The known-bad-anchor assertions are ALWAYS hard (they prove the
    /// scale still discriminates). The good-fixture bar becomes release-blocking only once this is set true —
    /// before calibration it is advisory (a below-bar good fixture logs, does not fail the run).
    /// </summary>
    public bool EnforceGoodFixtureBar { get; init; }

    public void EnsureValid()
    {
        if (MaxDimensionScore <= 0)
        {
            throw new InvalidOperationException("RuEvalOptions.MaxDimensionScore must be positive.");
        }

        Weights.EnsureValid();

        if (PassBar is < 0d || PassBar > MaxDimensionScore)
        {
            throw new InvalidOperationException(
                $"RuEvalOptions.PassBar must be within [0, {MaxDimensionScore}] (was {PassBar}).");
        }

        if (GroundednessFloor is < 0d || GroundednessFloor > MaxDimensionScore)
        {
            throw new InvalidOperationException(
                $"RuEvalOptions.GroundednessFloor must be within [0, {MaxDimensionScore}] (was {GroundednessFloor}).");
        }

        if (MinDimensionScore is < 0d || MinDimensionScore > MaxDimensionScore)
        {
            throw new InvalidOperationException(
                $"RuEvalOptions.MinDimensionScore must be within [0, {MaxDimensionScore}] (was {MinDimensionScore}).");
        }

        if (string.IsNullOrWhiteSpace(JudgeRouteKey))
        {
            throw new InvalidOperationException("RuEvalOptions.JudgeRouteKey must be non-empty.");
        }

        if (SampleCount < 1)
        {
            throw new InvalidOperationException("RuEvalOptions.SampleCount must be at least 1.");
        }
    }
}
