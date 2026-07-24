namespace SimCoach.RuEval;

/// <summary>
/// The RU-eval gate's config surface (Tier-2 dev-config, NOT a user slider): the rubric weights, the numeric
/// pass bar, the HARD groundedness floor, the judge route key, the per-fixture sample count, and the
/// gate-vs-advisory switch. Every threshold is here — no magic numbers in the harness. The owner-decided
/// stance is fixed (judge = <c>anthropic/claude-sonnet-4.6</c>, reference-anchored, 5-dim rubric + hard
/// groundedness floor, hard-fail known-bad anchors, good-fixture bar release-blocking after calibration).
/// <para>
/// CALIBRATED 2026-07-22 against 6 live judge runs on the committed fixtures (C1 / M18). Measured margins with
/// the values below: good fixtures composite ≥ 4.10 (clear PassBar 3.5 by ≥ 0.6, every dimension ≥ 3 vs the
/// 2.0 floor); the three known-bad anchors each tank exactly one dimension every run (fabricated groundedness
/// 0, raw-number tone 1, transliteration natural_russian 0) so the per-dimension floor rejects them with ≥ 1.0
/// margin regardless of composite. The good-fixture bar is now enforced (<see cref="EnforceGoodFixtureBar"/>).
/// The one flaky good fixture found during calibration — the debrief candidate leaking raw "мс" into
/// top_priority — was fixed at the source (debrief prompt rule 5), not by lowering the bar.
/// </para>
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
    /// scale still discriminates). The good-fixture bar is release-blocking when true; it defaulted to advisory
    /// (log, don't fail) until calibration proved the good fixtures clear the bar with margin. Enabled by default
    /// after the 2026-07-22 calibration run (see the type summary) — a below-bar good fixture now fails the gate.
    /// </summary>
    public bool EnforceGoodFixtureBar { get; init; } = true;

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
