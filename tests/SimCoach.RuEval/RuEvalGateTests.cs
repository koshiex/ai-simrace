using FluentAssertions;
using SimCoach.Coach;
using SimCoach.LLM;
using Xunit;
using Xunit.Abstractions;

namespace SimCoach.RuEval;

/// <summary>
/// The RU-quality gate as a REAL barrier (M18). Env-gated exactly like <c>GroundTruthRevalidationTests</c>: with
/// <c>SIMCOACH_RU_EVAL</c> unset (or no <c>OPENROUTER_API_KEY</c>) it returns early, so default <c>dotnet test</c>
/// runs fully offline. Under the release runner it drives the committed fixtures through the PRODUCTION
/// prompt+LLM path, has <c>anthropic/claude-sonnet-4.6</c> judge each candidate against its committed canonical
/// RU reference, and asserts the numeric bar + hard groundedness floor.
/// <para>
/// Enforcement (M18-gate decision): the two known-bad anchors are a HARD assertion — if either scores above the
/// bar the scale broke and the run fails. The good-fixture bar is release-blocking only once
/// <see cref="RuEvalOptions.EnforceGoodFixtureBar"/> is set (post-calibration); before that it is advisory
/// (logged). Any failing run dumps every candidate + verdict + justification via <see cref="ITestOutputHelper"/>.
/// </para>
/// <para>
/// Run-book: set <c>SIMCOACH_RU_EVAL=1</c> and <c>OPENROUTER_API_KEY</c>, then
/// <c>dotnet test tests/SimCoach.RuEval</c>. Fixture regen path: see <c>FixtureLoader</c>. Proto-free; no
/// runtime behaviour changes — this is the regression barrier for every prompt/gold edit in the pack.
/// </para>
/// </summary>
public sealed class RuEvalGateTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(3);

    private readonly ITestOutputHelper _out;

    public RuEvalGateTests(ITestOutputHelper outputHelper) => _out = outputHelper;

    [Fact]
    public async Task Candidates_clear_the_bar_and_known_bad_anchors_are_rejected()
    {
        if (!EnvGate.IsEnabled())
        {
            return; // Env-gated: offline CI lane skips cleanly (no network, no key). See the class doc / run-book.
        }

        var options = new RuEvalOptions();
        options.EnsureValid();
        var coachOptions = new CoachOptions();
        IReadOnlyList<EvalFixture> fixtures = FixtureLoader.Load();

        using var graph = new RuEvalGraph(options.JudgeRouteKey);
        ILlmClient llm = graph.Client;
        var judge = new RuJudge(llm, options);
        using var cts = new CancellationTokenSource(_timeout);

        var failures = new List<string>();
        foreach (EvalFixture fixture in fixtures)
        {
            CandidateResult candidate = await CandidateSource.GenerateAsync(fixture, llm, coachOptions, cts.Token);
            if (!candidate.FormatOk)
            {
                // Good fixtures must produce a well-formed candidate; the known-bad anchors always inject one.
                _out.WriteLine($"[{fixture.Id}] candidate NOT well-formed: {candidate.Detail}");
                failures.Add($"{fixture.Id}: production path produced a malformed candidate ({candidate.Detail}).");
                continue;
            }

            IReadOnlyList<JudgeVerdict> verdicts = await judge.JudgeAsync(fixture, candidate.PhraseRu, cts.Token);
            EvalOutcome outcome = ScoreAggregator.Evaluate(verdicts, options);
            Dump(fixture, candidate, verdicts, outcome);

            if (fixture.KnownBad)
            {
                if (outcome.Passed)
                {
                    failures.Add(
                        $"{fixture.Id}: KNOWN-BAD anchor scored above the bar (composite {outcome.Composite:0.00}, "
                        + $"groundedness {outcome.AvgGroundedness:0.00}) — the rubric scale broke.");
                }

                continue;
            }

            if (!outcome.Passed)
            {
                string message =
                    $"{fixture.Id}: good fixture below the bar (composite {outcome.Composite:0.00} < {options.PassBar}, "
                    + $"groundedness {outcome.AvgGroundedness:0.00} vs floor {options.GroundednessFloor}).";
                if (options.EnforceGoodFixtureBar)
                {
                    failures.Add(message);
                }
                else
                {
                    _out.WriteLine($"[advisory] {message}");
                }
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    private void Dump(EvalFixture fixture, CandidateResult candidate, IReadOnlyList<JudgeVerdict> verdicts, EvalOutcome outcome)
    {
        _out.WriteLine($"── {fixture.Id} ({fixture.Cadence}, knownBad={fixture.KnownBad}) ──");
        _out.WriteLine($"reference: {fixture.ReferencePhraseRu}");
        _out.WriteLine($"candidate: {candidate.PhraseRu}  [{candidate.Detail}]");
        foreach (JudgeVerdict v in verdicts)
        {
            _out.WriteLine(
                $"  g={v.Groundedness} b={v.Brevity} ru={v.NaturalRussian} act={v.Actionability} tone={v.Tone} :: {v.JustificationRu}");
        }

        _out.WriteLine(
            $"→ composite={outcome.Composite:0.00} groundedness={outcome.AvgGroundedness:0.00} "
            + $"bar={outcome.BarCleared} floor={outcome.FloorCleared} passed={outcome.Passed}");
    }
}
