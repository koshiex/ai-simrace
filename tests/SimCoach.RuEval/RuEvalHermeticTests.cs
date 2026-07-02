using System.Reflection;
using FluentAssertions;
using SimCoach.Coach;
using Xunit;

namespace SimCoach.RuEval;

/// <summary>
/// Always-on hermetic self-tests (no network, no provider chain) — they mirror the always-run helper fact in
/// <c>GroundTruthRevalidationTests</c> and pin the pure aggregator/parser/EnsureValid/env-gate code plus fixture
/// build validity. They must stay green in the offline CI lane; the network gate lives in
/// <see cref="RuEvalGateTests"/>.
/// </summary>
public sealed class RuEvalHermeticTests
{
    [Fact]
    public void Judge_system_prompt_is_embedded_in_the_main_manifest_not_a_satellite()
    {
        // Regression: the '.ru.' culture infix routes the resource to a satellite assembly unless
        // <WithCulture>false</WithCulture> pins it to the main manifest — RuJudge would throw when the gate runs.
        Assembly assembly = typeof(RuJudge).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream("SimCoach.RuEval.Prompts.ru-judge.system.ru.txt");
        stream.Should().NotBeNull("the judge prompt must live on the main manifest, not a ru/ satellite assembly");
    }

    [Fact]
    public void Composite_folds_dimensions_by_the_rubric_weights()
    {
        var weights = new RubricWeights();
        var verdict = new JudgeVerdict(5, 4, 5, 4, 5, "ок");

        // 5*0.35 + 4*0.15 + 5*0.15 + 4*0.20 + 5*0.15 = 4.65
        ScoreAggregator.Composite(verdict, weights).Should().BeApproximately(4.65d, 1e-9);
    }

    [Fact]
    public void Evaluate_passes_a_grounded_high_scoring_phrase()
    {
        var options = new RuEvalOptions();
        EvalOutcome outcome = ScoreAggregator.Evaluate([new JudgeVerdict(5, 4, 5, 4, 5, "ок")], options);

        outcome.Composite.Should().BeApproximately(4.65d, 1e-9);
        outcome.BarCleared.Should().BeTrue();
        outcome.FloorCleared.Should().BeTrue();
        outcome.Passed.Should().BeTrue();
    }

    [Fact]
    public void Hard_groundedness_floor_fails_a_fluent_but_ungrounded_phrase()
    {
        var options = new RuEvalOptions();

        // Groundedness 2 (< floor 3), everything else 5 → composite clears the bar yet the floor fails it.
        EvalOutcome outcome = ScoreAggregator.Evaluate([new JudgeVerdict(2, 5, 5, 5, 5, "красиво, но выдумано")], options);

        outcome.BarCleared.Should().BeTrue("a fluent phrase can still clear the weighted composite");
        outcome.FloorCleared.Should().BeFalse("groundedness 2 is below the hard floor");
        outcome.Passed.Should().BeFalse("the hard groundedness floor overrides the composite");
    }

    [Fact]
    public void EnsureValid_rejects_out_of_range_config()
    {
        FluentActions.Invoking(() => new RuEvalOptions { PassBar = 99d }.EnsureValid())
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new RuEvalOptions { SampleCount = 0 }.EnsureValid())
            .Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => new RuEvalOptions { Weights = new RubricWeights { Tone = 0.9d } }.EnsureValid())
            .Should().Throw<InvalidOperationException>("the weights no longer sum to 1.0");

        FluentActions.Invoking(() => new RuEvalOptions().EnsureValid()).Should().NotThrow();
    }

    [Fact]
    public void Verdict_parser_accepts_a_golden_verdict_and_rejects_malformed()
    {
        const string golden =
            "{\"groundedness\":5,\"brevity\":4,\"natural_russian\":5,\"actionability\":4,\"tone\":5,\"justification_ru\":\"ок\"}";
        VerdictParser.TryParse(golden, 5, out JudgeVerdict? verdict, out _).Should().BeTrue();
        verdict!.Groundedness.Should().Be(5);
        verdict.JustificationRu.Should().Be("ок");

        // Missing 'tone'.
        const string missing =
            "{\"groundedness\":5,\"brevity\":4,\"natural_russian\":5,\"actionability\":4,\"justification_ru\":\"ок\"}";
        VerdictParser.TryParse(missing, 5, out _, out _).Should().BeFalse();

        // Out of range (9 > max 5).
        const string outOfRange =
            "{\"groundedness\":9,\"brevity\":4,\"natural_russian\":5,\"actionability\":4,\"tone\":5,\"justification_ru\":\"ок\"}";
        VerdictParser.TryParse(outOfRange, 5, out _, out _).Should().BeFalse();

        VerdictParser.TryParse("not json", 5, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Env_gate_requires_both_the_flag_and_an_api_key()
    {
        EnvGate.Evaluate(null, "key").Should().BeFalse("unset SIMCOACH_RU_EVAL keeps the offline lane offline");
        EnvGate.Evaluate("1", null).Should().BeFalse("no OPENROUTER_API_KEY → skip");
        EnvGate.Evaluate(" ", "key").Should().BeFalse();
        EnvGate.Evaluate("1", "key").Should().BeTrue();
    }

    [Fact]
    public void Fixtures_build_through_the_production_gold_prompt_path()
    {
        IReadOnlyList<EvalFixture> fixtures = FixtureLoader.Load();

        fixtures.Should().HaveCount(6);
        fixtures.Should().Contain(f => f.KnownBad, "the scale is anchored by committed known-bad fixtures");
        fixtures.Count(f => f.KnownBad).Should().Be(3, "the transliteration + raw-number + fabricated-fact anchors");
        fixtures.Should().Contain(f => f.Cadence == CoachCadence.Session, "the debrief fixture");
        fixtures.Should().Contain(f => f.Cadence == CoachCadence.Corner && !f.HasReference, "the no-PB corner");

        foreach (EvalFixture fixture in fixtures)
        {
            fixture.ReferencePhraseRu.Should().NotBeNullOrWhiteSpace();
            fixture.FactsJson.Should().NotBeNullOrWhiteSpace();
            fixture.CandidateRequest.Should().NotBeNull();

            if (fixture.KnownBad)
            {
                fixture.CandidatePhraseRu.Should().NotBeNullOrWhiteSpace("a known-bad anchor injects a fixed phrase");
            }

            if (fixture.Cadence == CoachCadence.Corner)
            {
                fixture.SubsetIds.Should().NotBeEmpty("a corner candidate needs a non-empty action subset");
            }
        }
    }
}
