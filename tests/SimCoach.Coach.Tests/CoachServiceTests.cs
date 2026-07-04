using FluentAssertions;
using Microsoft.Extensions.Logging;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;
using SimCoach.Contracts.V1;
using SimCoach.LLM;
using SimCoach.Pipeline;
using SimCoach.Reference;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class CoachServiceTests
{
    private static readonly DateTimeOffset _now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Corner_llm_success_emits_an_llm_tip()
    {
        IReadOnlyList<CoachAction> subset = CornerSubset(hasReference: true);
        string chosen = subset[0].Id;
        var harness = new Harness(hasReference: true, Realtime(chosen, "Тормози позже немного."));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        CoachTip tip = harness.Sink.Tips[0];
        tip.Cadence.Should().Be(CoachCadence.Corner);
        tip.Source.Should().Be(TipSource.Llm);
        tip.ActionId.Should().Be(chosen);
        tip.PhraseRu.Should().Be("Тормози позже немного.");
        tip.ProviderModelId.Should().Be("google/gemini-2.5-flash-lite");
        tip.ActionLabelShort.Should().Be(subset[0].ActionLabelShort);
        // spa_t02 → GetShort short RU form; a revert of CornerInfo to the raw Italian "Eau Rouge" would fail here.
        tip.CornerName.Should().Be("О-Руж");
        tip.NoPbYet.Should().BeFalse();
        harness.Llm.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Corner_invalid_action_falls_back_to_template_without_retry()
    {
        IReadOnlyList<CoachAction> subset = CornerSubset(hasReference: true);
        var harness = new Harness(
            hasReference: true, RawSuccess("""{"action_id":"totally_invalid","phrase_ru":"x"}"""));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        CoachTip tip = harness.Sink.Tips[0];
        tip.Source.Should().Be(TipSource.Template);
        tip.ActionId.Should().Be(subset[0].Id); // the highest-priority action is rendered
        tip.CornerName.Should().Be("О-Руж"); // template path still speaks the RU short form, not "Eau Rouge"
        harness.Llm.Calls.Should().Be(1); // corner cadence never retries
    }

    [Fact]
    public async Task Lap_invalid_then_valid_retries_once()
    {
        IReadOnlyList<CoachAction> subset = LapSubset();
        string chosen = subset[0].Id;
        var harness = new Harness(
            hasReference: true,
            RawSuccess("""{"action_id":"totally_invalid","phrase_ru":"x"}"""),
            Realtime(chosen, "Береги резину этот круг."));

        await RunToCompletionAsync(harness, DomainEvent.Lap(GoldTestData.Lap()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Llm);
        harness.Sink.Tips[0].ActionId.Should().Be(chosen);
        harness.Llm.Calls.Should().Be(2);
        harness.Llm.Requests[1].SystemPrompt.Length
            .Should().BeGreaterThan(harness.Llm.Requests[0].SystemPrompt.Length); // retry reminder appended
    }

    [Fact]
    public async Task Retry_prompt_carries_the_rejection_reason_in_russian()
    {
        // M28: a validation-failing first answer (action_id outside the menu) must make the retry system prompt
        // echo a terse RU cause, so the model corrects the exact miss rather than re-guessing the schema.
        IReadOnlyList<CoachAction> subset = LapSubset();
        string chosen = subset[0].Id;
        var harness = new Harness(
            hasReference: true,
            RawSuccess("""{"action_id":"totally_invalid","phrase_ru":"x"}"""),
            Realtime(chosen, "Береги резину этот круг."));

        await RunToCompletionAsync(harness, DomainEvent.Lap(GoldTestData.Lap()));

        harness.Llm.Calls.Should().Be(2);
        harness.Llm.Requests[1].SystemPrompt
            .Should().Contain("Причина отказа: action_id не из разрешённого списка");
    }

    [Fact]
    public async Task Timeout_falls_back_to_template_without_retry()
    {
        var harness = new Harness(hasReference: true, new LlmResult.Failure(new LlmFailure.Timeout("slow")));

        await RunToCompletionAsync(harness, DomainEvent.Lap(GoldTestData.Lap()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Template);
        harness.Llm.Calls.Should().Be(1); // timeout is not retryable
    }

    [Fact]
    public async Task Debrief_llm_success_emits_an_llm_tip()
    {
        const string debrief =
            """{"top_losses":[{"corner":"Т1","ms":120,"why":"поздний тормоз"}],"top_priority":"Тормози раньше в Т1","setup_hint":"Снизь давление в шинах"}""";
        var harness = new Harness(hasReference: true, RawSuccess(debrief));

        await RunToCompletionAsync(harness, DomainEvent.Session(GoldTestData.Session()));

        harness.Sink.Tips.Should().ContainSingle();
        CoachTip tip = harness.Sink.Tips[0];
        tip.Cadence.Should().Be(CoachCadence.Session);
        tip.Source.Should().Be(TipSource.Llm);
        tip.PhraseRu.Should().Be("Тормози раньше в Т1");
        // The structured debrief payload is preserved on the tip (persisted to the reserved 004 columns).
        tip.TopLossesJson.Should().Contain("Т1");
        tip.SetupHint.Should().Be("Снизь давление в шинах");
        harness.Llm.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Debrief_retry_prompt_carries_the_rejection_reason_in_russian()
    {
        // M28: the widened TryAcceptDebrief surfaces the validator failure so the debrief retry echoes the RU
        // cause. First answer has an empty top_priority (quality miss → retryable); the second is valid.
        const string valid =
            """{"top_losses":[{"corner":"Т1","ms":120,"why":"поздний тормоз"}],"top_priority":"Тормози раньше в Т1","setup_hint":"Снизь давление"}""";
        var harness = new Harness(
            hasReference: true,
            RawSuccess("""{"top_losses":[],"top_priority":""}"""),
            RawSuccess(valid));

        await RunToCompletionAsync(harness, DomainEvent.Session(GoldTestData.Session()));

        harness.Llm.Calls.Should().Be(2);
        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Llm);
        harness.Llm.Requests[1].SystemPrompt.Should().Contain("Причина отказа: пустое поле top_priority");
    }

    [Fact]
    public async Task Debrief_llm_failure_falls_back_to_deterministic_template()
    {
        var harness = new Harness(hasReference: true, new LlmResult.Failure(new LlmFailure.Timeout("slow")));

        await RunToCompletionAsync(harness, DomainEvent.Session(GoldTestData.Session()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Template);
        harness.Sink.Tips[0].PhraseRu.Should().NotBeNullOrWhiteSpace(); // never an empty debrief
    }

    [Fact]
    public async Task No_pb_yet_is_set_when_reference_is_absent()
    {
        // No reference → only reference-free actions survive; the LLM call fails so the tip is a template,
        // still flagged no-PB-yet.
        var harness = new Harness(hasReference: false, new LlmResult.Failure(new LlmFailure.Timeout("slow")));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].NoPbYet.Should().BeTrue();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Template);
    }

    [Fact]
    public async Task Cancellation_drains_the_final_session_tip()
    {
        var harness = new Harness(
            hasReference: true,
            new LlmResult.Failure(new LlmFailure.Timeout("slow")),   // corner → template
            new LlmResult.Failure(new LlmFailure.Timeout("slow")));  // debrief → template
        harness.Session.Resolve("s1", _now);

        await harness.Service.StartAsync(CancellationToken.None); // loop parks at the empty channel
        Task stop = harness.Service.StopAsync(CancellationToken.None); // cancels → enters the drain
        harness.FanOut.Publish(DomainEvent.Corner(GoldTestData.Corner()));
        harness.FanOut.Publish(DomainEvent.Session(GoldTestData.Session()));
        harness.FanOut.Complete(); // lets the drain finish
        await stop;

        harness.Sink.Tips.Should().Contain(t => t.Cadence == CoachCadence.Session);
    }

    [Fact]
    public async Task Debrief_in_the_buffered_tail_is_processed_uncancelled_when_stop_races_it()
    {
        // Regression: a corner + the final Session debrief are already buffered when stop fires. The channel's
        // inner read drains both under one WaitToReadAsync pass, so the primary loop reads the debrief *after*
        // cancellation. It must still run on an uncancelled token — else (the original bug) its llm_usage write
        // is cancelled (TaskCanceledException) and the debrief tip is silently dropped.
        var sink = new CancelHonoringSink();
        var harness = new Harness(
            hasReference: true,
            sink,
            new LlmResult.Failure(new LlmFailure.Timeout("slow")),   // corner → template
            new LlmResult.Failure(new LlmFailure.Timeout("slow")));  // debrief → template
        harness.Session.Resolve("s1", _now);
        harness.Llm.OnCall = ordinal =>
        {
            if (ordinal == 1)
            {
                // Corner is in flight: request stop so the still-buffered Session is read under cancellation.
                _ = harness.Service.StopAsync(CancellationToken.None);
            }
            else if (ordinal == 2)
            {
                // Debrief reached: let both loops terminate once it finishes.
                harness.FanOut.Complete();
            }
        };

        harness.FanOut.Publish(DomainEvent.Corner(GoldTestData.Corner()));
        harness.FanOut.Publish(DomainEvent.Session(GoldTestData.Session()));
        await harness.Service.StartAsync(CancellationToken.None);
        await harness.Service.StopAsync(CancellationToken.None);

        harness.Llm.Tokens.Should().HaveCount(2);
        harness.Llm.Tokens[1].IsCancellationRequested.Should().BeFalse(); // debrief handled uncancelled
        sink.Tips.Should().Contain(t => t.Cadence == CoachCadence.Session); // and the tip survived the emit
    }

    [Fact]
    public async Task Llm_choosing_a_non_top_action_renders_that_action()
    {
        IReadOnlyList<CoachAction> subset = LapSubset();
        subset.Count.Should().BeGreaterThan(1); // need a non-top choice to exist
        CoachAction nonTop = subset[^1];
        var harness = new Harness(hasReference: true, Realtime(nonTop.Id, "Чуть аккуратнее в этом круге."));

        await RunToCompletionAsync(harness, DomainEvent.Lap(GoldTestData.Lap()));

        harness.Sink.Tips.Should().ContainSingle();
        CoachTip tip = harness.Sink.Tips[0];
        tip.Source.Should().Be(TipSource.Llm);
        tip.ActionId.Should().Be(nonTop.Id).And.NotBe(subset[0].Id);
        tip.ActionLabelShort.Should().Be(nonTop.ActionLabelShort);
    }

    [Fact]
    public async Task Second_corner_within_cooldown_is_suppressed()
    {
        // First corner speaks (LLM fails → template), arming the cooldown; the second is suppressed (no LLM call).
        var harness = new Harness(hasReference: true, new LlmResult.Failure(new LlmFailure.Timeout("slow")));

        // Two corners in one run land microseconds apart — well inside the 4 s corner cooldown.
        await RunToCompletionAsync(
            harness, DomainEvent.Corner(GoldTestData.Corner()), DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Llm.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Repeated_corner_on_the_next_lap_is_deduped_and_never_reaches_the_llm()
    {
        // M32: two identical (non-High) corner tips on consecutive laps. Cooldowns are disabled so the ONLY
        // thing that can silence the repeat is the cross-lap dedup gate. The first corner speaks (template) and
        // records tip.ActionId; the intervening lap summary speaks; the repeat corner is suppressed pre-LLM, so
        // it never reaches the LLM. Only two responses are scripted — a broken dedup would call a third and throw.
        var ruleOptions = new RuleEngineOptions
        {
            Cadence = new CadenceOptions { GlobalCooldown = TimeSpan.Zero, Cooldowns = NoCooldowns() },
        };
        var harness = new Harness(
            hasReference: true,
            ruleOptions,
            new LlmResult.Failure(new LlmFailure.Timeout("slow")),   // corner 1 → template, records the action
            new LlmResult.Failure(new LlmFailure.Timeout("slow")));  // lap summary → template

        await RunToCompletionAsync(
            harness,
            DomainEvent.Corner(UndersteerCorner()),
            DomainEvent.Lap(GoldTestData.Lap()),
            DomainEvent.Corner(UndersteerCorner()));

        IReadOnlyList<CoachTip> corners = harness.Sink.Tips.Where(t => t.Cadence == CoachCadence.Corner).ToList();
        corners.Should().ContainSingle();
        corners[0].ActionId.Should().Be("ease_understeer"); // the recorded action the repeat was matched against
        harness.Llm.Calls.Should().Be(2); // corner 1 + the lap summary; the repeat corner was deduped pre-LLM
    }

    private static IReadOnlyDictionary<CoachCadence, TimeSpan> NoCooldowns() =>
        Enum.GetValues<CoachCadence>().ToDictionary(c => c, _ => TimeSpan.Zero);

    // A corner whose lead action is the apex-phase ease_understeer (Medium severity, so the High-severity dedup
    // bypass does NOT apply) — the neutral corner trips no brake/entry action, so understeer alone leads.
    private static CornerEvent UndersteerCorner()
    {
        CornerEvent ev = GoldTestData.CornerNeutral();
        ev.DeltaMs = 140;
        ev.UndersteerScore = 0.71f;
        return ev;
    }

    [Fact]
    public async Task A_faulting_sink_is_isolated_and_the_drain_continues()
    {
        var sink = new ThrowOnceSink();
        var harness = new Harness(
            hasReference: true,
            sink,
            new LlmResult.Failure(new LlmFailure.Timeout("slow")),   // corner → template (emit throws)
            new LlmResult.Failure(new LlmFailure.Timeout("slow")));  // debrief → template (emit ok)

        // The corner tip's emit throws (isolated + logged); the session debrief must still be emitted.
        Func<Task> run = () => RunToCompletionAsync(
            harness, DomainEvent.Corner(GoldTestData.Corner()), DomainEvent.Session(GoldTestData.Session()));

        await run.Should().NotThrowAsync();
        sink.Tips.Should().Contain(t => t.Cadence == CoachCadence.Session);
    }

    [Fact]
    public async Task Over_budget_emits_a_template_budget_tip_without_calling_the_llm()
    {
        // Session cost already over the 0.50 default cap → the gate downgrades before any LLM call.
        var harness = new Harness(hasReference: true, sink: null, cost: new StubCost(sessionCostUsd: 1.00m));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.TemplateBudget);
        harness.Sink.Tips[0].CornerName.Should().Be("О-Руж"); // budget fallback keeps the RU short form
        harness.Llm.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Rejected_llm_answer_logs_the_validator_reason_not_none()
    {
        // Raw LLM success whose action_id is outside the menu → quality rejection. The M23 accept/fallback line
        // must carry the TipValidator reason, not the "none" sentinel reserved for a clean accept.
        var harness = new Harness(
            hasReference: true, RawSuccess("""{"action_id":"totally_invalid","phrase_ru":"x"}"""));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        string tipLine = TipOutcomeLine(harness);
        tipLine.Should().Contain("action_id 'totally_invalid' not in subset");
        tipLine.Should().NotContain("rejection=none");
    }

    [Fact]
    public async Task Infra_failure_logs_the_failure_variant_name_as_the_rejection()
    {
        // A non-Success result carries no validator reason; the M23 line must surface the infra failure variant
        // ("Timeout") so a transport/timeout miss is distinguishable from a model-quality rejection.
        var harness = new Harness(hasReference: true, new LlmResult.Failure(new LlmFailure.Timeout("slow")));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        TipOutcomeLine(harness).Should().Contain("rejection=Timeout");
    }

    [Fact]
    public async Task Weak_catch_all_none_abstains_without_emitting_or_arming_cooldown()
    {
        // Precondition: a corner where only the weak corner_catch_all fired (large delta, no specific trigger).
        IReadOnlyList<CoachAction> subset = CatchAllSubset();
        subset[0].Id.Should().Be("corner_catch_all");
        var harness = new Harness(
            hasReference: true,
            Realtime("none", "В повороте отклонение около 200."),   // first corner → abstain
            Realtime("none", "В повороте отклонение около 200."));  // second corner → abstain again

        // Two catch-all corners microseconds apart. If abstain armed the 4 s corner cooldown, the second would be
        // suppressed pre-LLM (1 call). It is not armed, so both reach the LLM (2 calls) and neither emits a tip.
        await RunToCompletionAsync(
            harness, DomainEvent.Corner(CatchAllCorner()), DomainEvent.Corner(CatchAllCorner()));

        harness.Sink.Tips.Should().BeEmpty();
        harness.Llm.Calls.Should().Be(2);
        harness.Logger.Snapshot().Should().Contain(e =>
            e.Level == LogLevel.Information && e.Message.StartsWith("Coach abstain", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Leaked_none_when_abstain_not_offered_falls_back_to_template_not_silence()
    {
        // A specific action leads (abstain not offered), yet the model returns "none" → not in subset → template.
        IReadOnlyList<CoachAction> subset = CornerSubset(hasReference: true);
        subset[0].Id.Should().NotBe("corner_catch_all");
        var harness = new Harness(hasReference: true, Realtime("none", "тишина"));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Template);
        harness.Sink.Tips[0].ActionId.Should().Be(subset[0].Id);
        harness.Llm.Calls.Should().Be(1); // corner never retries; a leaked none is a plain rejection
    }

    [Fact]
    public async Task Llm_accept_logs_the_parsed_confidence()
    {
        // M31: an accepted LLM tip that self-reports "low" surfaces confidence=Low on the M23 accept line.
        // Confidence is parsed tolerantly regardless of RequestConfidence (which only shapes the schema/prompt).
        IReadOnlyList<CoachAction> subset = CornerSubset(hasReference: true);
        string chosen = subset[0].Id;
        var harness = new Harness(
            hasReference: true,
            RawSuccess($$"""{"action_id":"{{chosen}}","phrase_ru":"Тормози позже.","confidence":"low"}"""));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Llm); // emit-vs-silent unchanged by confidence
        TipOutcomeLine(harness).Should().Contain("confidence=Low");
    }

    [Fact]
    public async Task Template_fallback_logs_the_high_confidence_default()
    {
        // A non-Success miss → template fallback; confidence defaults to High (no model self-report).
        var harness = new Harness(hasReference: true, new LlmResult.Failure(new LlmFailure.Timeout("slow")));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips[0].Source.Should().Be(TipSource.Template);
        TipOutcomeLine(harness).Should().Contain("confidence=High");
    }

    private static string TipOutcomeLine(Harness harness) =>
        harness.Logger.Snapshot()
            .Single(e => e.Level == LogLevel.Information && e.Message.StartsWith("Coach tip", StringComparison.Ordinal))
            .Message;

    private static async Task RunToCompletionAsync(Harness harness, params DomainEvent[] events)
    {
        harness.Session.Resolve("s1", _now);
        for (int i = 0; i < events.Length; i++)
        {
            harness.FanOut.Publish(events[i]);
        }

        harness.FanOut.Complete();
        await harness.Service.StartAsync(CancellationToken.None);
        await harness.Service.StopAsync(CancellationToken.None);
    }

    private static IReadOnlyList<CoachAction> CornerSubset(bool hasReference)
    {
        var options = new CoachOptions();
        var builder = new GoldArtifactBuilder(CornerNameMap.Load(), options);
        GoldArtifact<GoldCornerEvent> gold =
            builder.BuildCorner(GoldTestData.Corner(), new GoldSessionContext("spa", "gt3", "dry-cool", 1, hasReference));
        IGoldView view = GoldView.For(gold);
        return ActionRegistry.Load().ValidSubset(view, options);
    }

    // A corner where the large delta trips only corner_catch_all (no specific-action clause holds) → the weak
    // catch-all leads the subset and abstain is offered.
    private static CornerEvent CatchAllCorner()
    {
        CornerEvent ev = GoldTestData.CornerNeutral();
        ev.DeltaMs = 200;
        return ev;
    }

    private static IReadOnlyList<CoachAction> CatchAllSubset()
    {
        var options = new CoachOptions();
        var builder = new GoldArtifactBuilder(CornerNameMap.Load(), options);
        GoldArtifact<GoldCornerEvent> gold =
            builder.BuildCorner(CatchAllCorner(), new GoldSessionContext("spa", "gt3", "dry-cool", 1, true));
        return ActionRegistry.Load().ValidSubset(GoldView.For(gold), options);
    }

    private static IReadOnlyList<CoachAction> LapSubset()
    {
        var options = new CoachOptions();
        var builder = new GoldArtifactBuilder(CornerNameMap.Load(), options);
        GoldArtifact<GoldLapEvent> gold =
            builder.BuildLap(GoldTestData.Lap(), new GoldSessionContext("spa", "gt3", "dry-cool", 7, true));
        IGoldView view = GoldView.For(gold);
        return ActionRegistry.Load().ValidSubset(view, options);
    }

    private static LlmResult Realtime(string actionId, string phrase) =>
        RawSuccess($$"""{"action_id":"{{actionId}}","phrase_ru":"{{phrase}}"}""");

    private static LlmResult RawSuccess(string json) =>
        new LlmResult.Success(
            json,
            new LlmUsage(100, 20),
            new LlmCallInfo("openrouter-google", "google/gemini-2.5-flash-lite", TimeSpan.FromMilliseconds(200), "stop"));

    private sealed class Harness
    {
        public Harness(bool hasReference, params LlmResult[] responses)
            : this(hasReference, null, null, null, responses)
        {
        }

        public Harness(bool hasReference, ICoachTipSink? sink, params LlmResult[] responses)
            : this(hasReference, sink, null, null, responses)
        {
        }

        public Harness(bool hasReference, RuleEngineOptions ruleOptions, params LlmResult[] responses)
            : this(hasReference, null, null, ruleOptions, responses)
        {
        }

        public Harness(bool hasReference, ICoachTipSink? sink, ICostQueryRepository? cost, params LlmResult[] responses)
            : this(hasReference, sink, cost, null, responses)
        {
        }

        public Harness(
            bool hasReference, ICoachTipSink? sink, ICostQueryRepository? cost, RuleEngineOptions? ruleOptions,
            params LlmResult[] responses)
        {
            Llm = new ScriptedLlm(responses);
            ICoachTipSink effectiveSink = sink ?? Sink;
            Cost = cost ?? new StubCost();
            var coachOptions = new CoachOptions();
            var names = CornerNameMap.Load();
            var ambient = new StubAmbient(
                new GoldSessionContext("spa", "gt3", "dry-cool", 7, hasReference), GateSnapshot.Unknown);
            Service = new CoachService(
                FanOut,
                new GoldArtifactBuilder(names, coachOptions),
                ActionRegistry.Load(),
                new PromptBuilder(coachOptions, new PromptOptions()),
                Llm,
                new RuleEngine(ruleOptions ?? new RuleEngineOptions(), TimeProvider.System),
                effectiveSink,
                ambient,
                names,
                coachOptions,
                Cost,
                Session,
                TimeProvider.System,
                Logger);
        }

        public CollectingLogger<CoachService> Logger { get; } = new();

        public DomainEventFanOut FanOut { get; } = new();

        public CapturingSink Sink { get; } = new();

        public ScriptedLlm Llm { get; }

        public ICostQueryRepository Cost { get; }

        public SessionContext Session { get; } = new();

        public CoachService Service { get; }
    }

    // Zero cost by default (budget never trips); a non-zero session cost exercises the budget downgrade.
    private sealed class StubCost(decimal sessionCostUsd = 0m) : ICostQueryRepository
    {
        public Task<CostSummary> GetSessionCostAsync(string sessionId, CancellationToken ct) =>
            Task.FromResult(new CostSummary(0, (double)sessionCostUsd, 0, 0, 0));

        public Task<RollingCost> GetRolling30DayCostAsync(CancellationToken ct) =>
            Task.FromResult(new RollingCost(0, 0d));

        public Task<IReadOnlyList<CostByDay>> GetCostByDayAsync(int days, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CostByDay>>([]);

        public Task<IReadOnlyList<CostByRoute>> GetCostByRouteAsync(DateTimeOffset fromUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CostByRoute>>([]);
    }

    private sealed class CapturingSink : ICoachTipSink
    {
        public List<CoachTip> Tips { get; } = [];

        public Task EmitTipAsync(CoachTip tip, CancellationToken ct)
        {
            Tips.Add(tip);
            return Task.CompletedTask;
        }
    }

    // Mirrors the live SQLite sink: a cancelled token fails the emit (Dapper throws TaskCanceledException), so
    // a tip processed under a cancelled token is lost. Used to prove the debrief survives a racing stop.
    private sealed class CancelHonoringSink : ICoachTipSink
    {
        public List<CoachTip> Tips { get; } = [];

        public Task EmitTipAsync(CoachTip tip, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Tips.Add(tip);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowOnceSink : ICoachTipSink
    {
        private bool _thrown;

        public List<CoachTip> Tips { get; } = [];

        public Task EmitTipAsync(CoachTip tip, CancellationToken ct)
        {
            if (!_thrown)
            {
                _thrown = true;
                throw new InvalidOperationException("sink boom");
            }

            Tips.Add(tip);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAmbient : ICoachAmbientState
    {
        private readonly GoldSessionContext _metadata;
        private readonly GateSnapshot _gate;

        public StubAmbient(GoldSessionContext metadata, GateSnapshot gate)
        {
            _metadata = metadata;
            _gate = gate;
        }

        public GoldSessionContext SessionMetadata() => _metadata;

        public GateSnapshot LatestGate() => _gate;
    }

    private sealed class ScriptedLlm : ILlmClient
    {
        private readonly LlmResult[] _responses;
        private int _index;

        public ScriptedLlm(LlmResult[] responses) => _responses = responses;

        public List<LlmRequest> Requests { get; } = [];

        // The token each call was invoked with, in order — lets a test assert the shutdown drain never runs a
        // handler (esp. the debrief) under an already-cancelled token.
        public List<CancellationToken> Tokens { get; } = [];

        // Fired per call after the token is captured; the arg is the 1-based call ordinal. A test uses it to
        // race stop against a still-buffered tail.
        public Action<int>? OnCall { get; set; }

        public int Calls => Requests.Count;

        public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            // Yield so StartAsync has returned (BackgroundService._executeTask assigned) before OnCall can
            // request stop; otherwise the whole loop runs inside StartAsync and StopAsync no-ops.
            await Task.Yield();
            Requests.Add(request);
            Tokens.Add(ct);
            OnCall?.Invoke(Requests.Count);
            if (_index >= _responses.Length)
            {
                throw new InvalidOperationException("Unexpected extra LLM call.");
            }

            return _responses[_index++];
        }

        public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
