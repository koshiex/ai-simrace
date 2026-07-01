using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;
using SimCoach.Contracts.V1;
using SimCoach.LLM;
using SimCoach.Pipeline;
using SimCoach.Reference;
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
        var harness = new Harness(llmLive: true, hasReference: true, Realtime(chosen, "Тормози позже немного."));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        CoachTip tip = harness.Sink.Tips[0];
        tip.Cadence.Should().Be(CoachCadence.Corner);
        tip.Source.Should().Be(TipSource.Llm);
        tip.ActionId.Should().Be(chosen);
        tip.PhraseRu.Should().Be("Тормози позже немного.");
        tip.ProviderModelId.Should().Be("google/gemini-2.5-flash-lite");
        tip.ActionLabelShort.Should().Be(subset[0].ActionLabelShort);
        tip.CornerName.Should().NotBeNullOrWhiteSpace();
        tip.NoPbYet.Should().BeFalse();
        harness.Llm.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Corner_invalid_action_falls_back_to_template_without_retry()
    {
        IReadOnlyList<CoachAction> subset = CornerSubset(hasReference: true);
        var harness = new Harness(
            llmLive: true, hasReference: true, RawSuccess("""{"action_id":"totally_invalid","phrase_ru":"x"}"""));

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        CoachTip tip = harness.Sink.Tips[0];
        tip.Source.Should().Be(TipSource.Template);
        tip.ActionId.Should().Be(subset[0].Id); // the highest-priority action is rendered
        harness.Llm.Calls.Should().Be(1); // corner cadence never retries
    }

    [Fact]
    public async Task Lap_invalid_then_valid_retries_once()
    {
        IReadOnlyList<CoachAction> subset = LapSubset();
        string chosen = subset[0].Id;
        var harness = new Harness(
            llmLive: true,
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
    public async Task Timeout_falls_back_to_template_without_retry()
    {
        var harness = new Harness(llmLive: true, hasReference: true, new LlmResult.Failure(new LlmFailure.Timeout("slow")));

        await RunToCompletionAsync(harness, DomainEvent.Lap(GoldTestData.Lap()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Template);
        harness.Llm.Calls.Should().Be(1); // timeout is not retryable
    }

    [Fact]
    public async Task Debrief_uses_template_when_llm_is_disabled()
    {
        var harness = new Harness(llmLive: false, hasReference: true);

        await RunToCompletionAsync(harness, DomainEvent.Session(GoldTestData.Session()));

        harness.Sink.Tips.Should().ContainSingle();
        CoachTip tip = harness.Sink.Tips[0];
        tip.Cadence.Should().Be(CoachCadence.Session);
        tip.Source.Should().Be(TipSource.Template);
        tip.PhraseRu.Should().NotBeNullOrWhiteSpace();
        harness.Llm.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Debrief_llm_failure_falls_back_to_deterministic_template()
    {
        var harness = new Harness(llmLive: true, hasReference: true, new LlmResult.Failure(new LlmFailure.Timeout("slow")));

        await RunToCompletionAsync(harness, DomainEvent.Session(GoldTestData.Session()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Template);
        harness.Sink.Tips[0].PhraseRu.Should().NotBeNullOrWhiteSpace(); // never an empty debrief
    }

    [Fact]
    public async Task No_pb_yet_is_set_when_reference_is_absent()
    {
        var harness = new Harness(llmLive: false, hasReference: false);

        await RunToCompletionAsync(harness, DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
        harness.Sink.Tips[0].NoPbYet.Should().BeTrue();
        harness.Sink.Tips[0].Source.Should().Be(TipSource.Template);
    }

    [Fact]
    public async Task Cancellation_drains_the_final_session_tip()
    {
        var harness = new Harness(llmLive: false, hasReference: true);
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
    public async Task Llm_choosing_a_non_top_action_renders_that_action()
    {
        IReadOnlyList<CoachAction> subset = LapSubset();
        subset.Count.Should().BeGreaterThan(1); // need a non-top choice to exist
        CoachAction nonTop = subset[^1];
        var harness = new Harness(llmLive: true, hasReference: true, Realtime(nonTop.Id, "Чуть аккуратнее в этом круге."));

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
        var harness = new Harness(llmLive: false, hasReference: true);

        // Two corners in one run land microseconds apart — well inside the 4 s corner cooldown.
        await RunToCompletionAsync(
            harness, DomainEvent.Corner(GoldTestData.Corner()), DomainEvent.Corner(GoldTestData.Corner()));

        harness.Sink.Tips.Should().ContainSingle();
    }

    [Fact]
    public async Task A_faulting_sink_is_isolated_and_the_drain_continues()
    {
        var sink = new ThrowOnceSink();
        var harness = new Harness(llmLive: false, hasReference: true, sink);

        // The corner tip's emit throws (isolated + logged); the session debrief must still be emitted.
        Func<Task> run = () => RunToCompletionAsync(
            harness, DomainEvent.Corner(GoldTestData.Corner()), DomainEvent.Session(GoldTestData.Session()));

        await run.Should().NotThrowAsync();
        sink.Tips.Should().Contain(t => t.Cadence == CoachCadence.Session);
    }

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
        public Harness(bool llmLive, bool hasReference, params LlmResult[] responses)
            : this(llmLive, hasReference, null, responses)
        {
        }

        public Harness(bool llmLive, bool hasReference, ICoachTipSink? sink, params LlmResult[] responses)
        {
            Llm = new ScriptedLlm(responses);
            ICoachTipSink effectiveSink = sink ?? Sink;
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
                new RuleEngine(new RuleEngineOptions(), TimeProvider.System),
                effectiveSink,
                ambient,
                names,
                coachOptions,
                new CoachServiceOptions { LlmLive = llmLive },
                Session,
                TimeProvider.System,
                NullLogger<CoachService>.Instance);
        }

        public DomainEventFanOut FanOut { get; } = new();

        public CapturingSink Sink { get; } = new();

        public ScriptedLlm Llm { get; }

        public SessionContext Session { get; } = new();

        public CoachService Service { get; }
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

        public int Calls => Requests.Count;

        public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            if (_index >= _responses.Length)
            {
                throw new InvalidOperationException("Unexpected extra LLM call.");
            }

            return Task.FromResult(_responses[_index++]);
        }

        public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
