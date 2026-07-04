using FluentAssertions;
using SimCoach.Coach;
using SimCoach.LLM;
using SimCoach.Storage.Repositories;
using Xunit;
using Xunit.Abstractions;

namespace SimCoach.RuEval;

/// <summary>
/// The M30 A/B shadow-harness: an ADVISORY comparison that fans the same committed real fixtures across the
/// candidate one-liner models, reuses the shipped M18 judge/rubric to score each, and reads the per-call
/// <c>llm_usage</c> ledger for cost — so the corner/sector default can later be chosen from data. Owner
/// decision: gemini-only first cut, advisory-only (never fails on ranking), measures only (no
/// <c>appsettings.json</c> route change). The pure <see cref="AbScorecard"/> reducer and
/// <see cref="AbHarnessOptions"/> validation are pinned by always-on hermetic tests that stay green on the
/// offline lane; the network fan-out is env-gated exactly like <see cref="RuEvalGateTests"/>.
/// </summary>
public sealed class AbHarnessTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(5);

    private readonly ITestOutputHelper _out;

    public AbHarnessTests(ITestOutputHelper outputHelper) => _out = outputHelper;

    [Fact]
    public void Default_candidate_routes_resolve_to_distinct_models()
    {
        var options = new AbHarnessOptions();
        options.EnsureValid();

        IReadOnlyList<string> models = [.. options.Candidates.Select(RuEvalGraph.ModelIdFor)];

        models.Should().OnlyHaveUniqueItems("an A/B comparison is meaningless if two candidate routes share a model");
        models.Should().HaveCount(options.Candidates.Count);
        models.Should().OnlyContain(m => !string.IsNullOrWhiteSpace(m));
    }

    [Fact]
    public void EnsureValid_rejects_empty_duplicate_and_underflowing_config()
    {
        FluentActions.Invoking(() => new AbHarnessOptions { Candidates = [] }.EnsureValid())
            .Should().Throw<InvalidOperationException>("an empty candidate list has nothing to compare");
        FluentActions.Invoking(() => new AbHarnessOptions { Candidates = ["ab_gemini_25", "ab_gemini_25"] }.EnsureValid())
            .Should().Throw<InvalidOperationException>("a duplicate candidate double-counts one model");
        FluentActions.Invoking(() => new AbHarnessOptions { Candidates = ["ab_gemini_25", " "] }.EnsureValid())
            .Should().Throw<InvalidOperationException>("a blank route key cannot resolve to a model");
        FluentActions.Invoking(() => new AbHarnessOptions { SampleCount = 0 }.EnsureValid())
            .Should().Throw<InvalidOperationException>("at least one judge sample is required");

        FluentActions.Invoking(() => new AbHarnessOptions().EnsureValid()).Should().NotThrow();
    }

    [Fact]
    public void Rank_orders_by_composite_and_excludes_format_rejects_from_the_average()
    {
        var weights = new RubricWeights();
        var strong = new JudgeVerdict(5, 4, 5, 4, 5, "сильный");  // composite 4.65
        var weak = new JudgeVerdict(3, 3, 3, 3, 3, "слабый");     // composite 3.00

        // Candidate A: two well-formed fixtures (4.65 each) + one malformed reject. The reject must NOT drag the
        // 4.65 average toward (4.65+4.65+0)/3 = 3.10 — it is counted separately, never averaged in.
        // Candidate B: two well-formed fixtures at 3.00.
        AbFixtureSample[] samples =
        [
            new("A", "m-a", "f1", true, [strong], 0.0010d, 100d),
            new("A", "m-a", "f2", true, [strong], 0.0010d, 100d),
            new("A", "m-a", "f3", false, [], 0.0010d, 50d),
            new("B", "m-b", "f1", true, [weak], 0.0020d, 200d),
            new("B", "m-b", "f2", true, [weak], 0.0020d, 200d),
        ];

        IReadOnlyList<AbCandidateOutcome> ranked = AbScorecard.Rank(samples, weights);

        ranked.Should().HaveCount(2);
        ranked[0].RouteKey.Should().Be("A", "the higher composite ranks first");
        ranked[0].Composite.Should().BeApproximately(4.65d, 1e-9, "the format-reject row must not corrupt the average");
        ranked[0].JudgedFixtures.Should().Be(2);
        ranked[0].FormatRejects.Should().Be(1);
        ranked[0].FormatRejectRate.Should().BeApproximately(1d / 3d, 1e-9);
        ranked[0].TotalCostUsd.Should().BeApproximately(0.0030d, 1e-9, "cost sums over EVERY attempt, reject included");

        ranked[1].RouteKey.Should().Be("B");
        ranked[1].Composite.Should().BeApproximately(3.00d, 1e-9);
        ranked[1].FormatRejects.Should().Be(0);
    }

    [Fact]
    public void Rank_breaks_a_composite_tie_by_cheaper_total_cost()
    {
        var weights = new RubricWeights();
        var even = new JudgeVerdict(4, 4, 4, 4, 4, "ровно");  // composite 4.00 (weights sum to 1)

        AbFixtureSample[] samples =
        [
            new("expensive", "m-x", "f1", true, [even], 0.0050d, 120d),
            new("cheap", "m-y", "f1", true, [even], 0.0030d, 120d),
        ];

        IReadOnlyList<AbCandidateOutcome> ranked = AbScorecard.Rank(samples, weights);

        ranked[0].Composite.Should().BeApproximately(ranked[1].Composite, 1e-9, "the two candidates are quality-tied");
        ranked[0].RouteKey.Should().Be("cheap", "the cost tiebreak favours the cheaper candidate");
    }

    [Fact]
    public async Task Ab_harness_emits_a_ranked_scorecard_over_the_real_fixtures()
    {
        if (!EnvGate.IsEnabled())
        {
            return; // Env-gated: offline CI lane skips cleanly (no network, no key). See the RuEvalGateTests run-book.
        }

        var ab = new AbHarnessOptions();
        ab.EnsureValid();
        var evalOptions = new RuEvalOptions { SampleCount = ab.SampleCount };
        evalOptions.EnsureValid();
        var coachOptions = new CoachOptions();

        IReadOnlyList<EvalFixture> fixtures = [.. FixtureLoader.Load().Where(f => !f.KnownBad)];
        fixtures.Should().NotBeEmpty("the A/B comparison needs at least one real (non-known-bad) fixture");

        using var graph = new RuEvalGraph(evalOptions.JudgeRouteKey);
        ILlmClient llm = graph.Client;
        var judge = new RuJudge(llm, evalOptions);
        using var cts = new CancellationTokenSource(_timeout);

        var records =
            new List<(string Candidate, string Model, string FixtureId, bool FormatOk,
                IReadOnlyList<JudgeVerdict> Verdicts, double LatencyMs)>();
        foreach (string candidate in ab.Candidates)
        {
            string model = RuEvalGraph.ModelIdFor(candidate);
            foreach (EvalFixture fixture in fixtures)
            {
                LlmResult result = await llm.CompleteAsync(fixture.CandidateRequest with { RouteKey = candidate }, cts.Token);
                if (result is not LlmResult.Success success)
                {
                    var failure = (LlmResult.Failure)result;
                    _out.WriteLine($"[{candidate}/{fixture.Id}] generation failed: {failure.Error}");
                    records.Add((candidate, model, fixture.Id, false, [], 0d));
                    continue;
                }

                double latencyMs = success.Info.Latency.TotalMilliseconds;
                (bool ok, string phrase) = Validate(fixture, success.Json, coachOptions);
                if (!ok)
                {
                    records.Add((candidate, model, fixture.Id, false, [], latencyMs));
                    continue;
                }

                IReadOnlyList<JudgeVerdict> verdicts = await judge.JudgeAsync(fixture, phrase, cts.Token);
                records.Add((candidate, model, fixture.Id, true, verdicts, latencyMs));
            }
        }

        IReadOnlyList<CostByRoute> ledger = await graph.CostQuery.GetCostByRouteAsync(DateTimeOffset.UnixEpoch, cts.Token);
        var avgCostByRoute = ledger
            .Where(r => r.CallCount > 0)
            .ToDictionary(r => r.RouteKey, r => r.CostUsd / r.CallCount, StringComparer.Ordinal);

        IReadOnlyList<AbFixtureSample> samples =
        [
            .. records.Select(r => new AbFixtureSample(
                r.Candidate, r.Model, r.FixtureId, r.FormatOk, r.Verdicts,
                avgCostByRoute.TryGetValue(r.Candidate, out double cost) ? cost : 0d, r.LatencyMs)),
        ];

        IReadOnlyList<AbCandidateOutcome> scorecard = AbScorecard.Rank(samples, evalOptions.Weights);
        DumpScorecard(scorecard);

        // Advisory-only (owner decision): the harness MEASURES, it never fails on ranking. Only non-ranking
        // sanity assertions hold — a non-empty scorecard and distinct candidate models.
        scorecard.Should().NotBeEmpty("the harness must tabulate at least one candidate");
        scorecard.Select(o => o.ModelId).Should().OnlyHaveUniqueItems("each candidate route must resolve to a distinct model");
    }

    private static (bool Ok, string Phrase) Validate(EvalFixture fixture, string json, CoachOptions coachOptions)
    {
        if (fixture.Cadence == CoachCadence.Session)
        {
            bool ok = TipValidator.TryValidateDebrief(
                json, coachOptions.MaxDebriefLosses, coachOptions.DebriefMaxWords, out string topPriority, out _);
            return (ok, ok ? topPriority : string.Empty);
        }

        RealtimeTipVerdict verdict = TipValidator.TryValidateRealtime(
            json, fixture.SubsetIds, coachOptions.InCornerMaxWords, allowAbstain: false, out _, out string phrase, out _, out _);
        return verdict == RealtimeTipVerdict.Accept ? (true, phrase) : (false, string.Empty);
    }

    private void DumpScorecard(IReadOnlyList<AbCandidateOutcome> scorecard)
    {
        _out.WriteLine("── A/B one-liner scorecard (advisory) ──");
        int rank = 1;
        foreach (AbCandidateOutcome o in scorecard)
        {
            _out.WriteLine(
                $"#{rank++} {o.RouteKey} ({o.ModelId}): composite={o.Composite:0.00} "
                + $"[g={o.Groundedness:0.00} b={o.Brevity:0.00} ru={o.NaturalRussian:0.00} "
                + $"act={o.Actionability:0.00} tone={o.Tone:0.00}] "
                + $"cost=${o.TotalCostUsd:0.0000} latency={o.AvgLatencyMs:0}ms "
                + $"rejects={o.FormatRejects}/{o.Calls} ({o.FormatRejectRate:P0})");
        }
    }
}
