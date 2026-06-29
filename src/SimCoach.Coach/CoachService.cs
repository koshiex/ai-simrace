using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Rules;
using SimCoach.Contracts.V1;
using SimCoach.LLM;
using SimCoach.Pipeline;
using SimCoach.Reference;
using SimCoach.Storage.Repositories;

namespace SimCoach.Coach;

/// <summary>
/// The coaching engine, wired into the live host as a <see cref="BackgroundService"/>. Subscribes to the
/// lossless <see cref="DomainEventFanOut"/> in its constructor (so it cannot miss the opening events) and,
/// per domain event, runs GoldArtifactBuilder → valid-subset → <see cref="RuleEngine"/> → (LLM | template)
/// → cadence-aware validation/retry → <see cref="ICoachTipSink"/>. On shutdown it drains the buffered tail
/// to channel completion (not on the cancelled token) so the final <c>SessionEvent</c> debrief survives stop.
/// It always calls <c>ILlmClient</c>; the fake-vs-real provider choice lives in the router behind the single
/// <c>Llm:Live</c> flag (off by default → FakeProvider, so replay/CI need no API key). The per-session and
/// rolling-monthly budget caps downgrade to a template (<see cref="TipSource.TemplateBudget"/>) before any LLM
/// call; the budget is read from <c>ICostQueryRepository</c>, cached and refreshed after each handled event.
/// </summary>
public sealed class CoachService : BackgroundService
{
    private const string RetryVersion = "v1";

    private readonly DomainEventSubscription _subscription;
    private readonly GoldArtifactBuilder _builder;
    private readonly ActionRegistry _registry;
    private readonly PromptBuilder _promptBuilder;
    private readonly ILlmClient _llm;
    private readonly RuleEngine _ruleEngine;
    private readonly ICoachTipSink _sink;
    private readonly ICoachAmbientState _ambient;
    private readonly CornerNameMap _names;
    private readonly CoachOptions _coachOptions;
    private readonly ICostQueryRepository _cost;
    private readonly SessionContext _sessionContext;
    private readonly TimeProvider _clock;
    private readonly ILogger<CoachService> _logger;
    private readonly string _retryReminder;

    private int _currentLap = 1;
    private BudgetState _budget = BudgetState.Zero;

    public CoachService(
        DomainEventFanOut fanOut,
        GoldArtifactBuilder builder,
        ActionRegistry registry,
        PromptBuilder promptBuilder,
        ILlmClient llm,
        RuleEngine ruleEngine,
        ICoachTipSink sink,
        ICoachAmbientState ambient,
        CornerNameMap names,
        CoachOptions coachOptions,
        ICostQueryRepository cost,
        SessionContext sessionContext,
        TimeProvider clock,
        ILogger<CoachService> logger)
    {
        ArgumentNullException.ThrowIfNull(fanOut);
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(ruleEngine);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(ambient);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(coachOptions);
        ArgumentNullException.ThrowIfNull(cost);
        ArgumentNullException.ThrowIfNull(sessionContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        coachOptions.EnsureValid();

        _builder = builder;
        _registry = registry;
        _promptBuilder = promptBuilder;
        _llm = llm;
        _ruleEngine = ruleEngine;
        _sink = sink;
        _ambient = ambient;
        _names = names;
        _coachOptions = coachOptions;
        _cost = cost;
        _sessionContext = sessionContext;
        _clock = clock;
        _logger = logger;
        _retryReminder = PromptResources.ReadRetryReminder(RetryVersion);
        _subscription = fanOut.Subscribe("coach");
    }

    public override void Dispose()
    {
        _subscription.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SessionIdentity identity;
        try
        {
            identity = await _sessionContext.Ready.WaitAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return; // shut down before a session ever started
        }

        _ruleEngine.ResetSession();

        try
        {
            // Seed the budget: session spend starts at 0, but the rolling 30-day total carries prior sessions.
            // Inside the try so a shutdown landing during the seed is handled like any other cancel (drain tail).
            await RefreshBudgetAsync(identity.SessionId, stoppingToken).ConfigureAwait(false);

            await foreach (DomainEvent ev in _subscription.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await HandleSafelyAsync(ev, identity.SessionId, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown: drain the buffered tail (incl. the final SessionEvent debrief) to channel
            // completion on CancellationToken.None — ComputeService completes the fan-out even under cancel,
            // so this cannot hang, and a token-bound read would have dropped exactly that tail.
            await foreach (DomainEvent ev in _subscription.ReadAllAsync(CancellationToken.None).ConfigureAwait(false))
            {
                await HandleSafelyAsync(ev, identity.SessionId, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _logger.LogInformation("Coach stopped for session {Session}", identity.SessionId);
        }
    }

    // Coaching is best-effort: one malformed event must not fault the BackgroundService (which, under the
    // default StopHost behavior, would tear down the whole host) or abort the shutdown drain. A cancellation
    // tied to the live token is rethrown so ExecuteAsync still transitions into the drain.
    private async Task HandleSafelyAsync(DomainEvent domainEvent, string sessionId, CancellationToken ct)
    {
        try
        {
            await HandleAsync(domainEvent, sessionId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Coach dropped a {Kind} event after a handler fault", domainEvent.Kind);
        }
    }

    private async Task HandleAsync(DomainEvent domainEvent, string sessionId, CancellationToken ct)
    {
        switch (domainEvent.Kind)
        {
            case DomainEventKind.Corner:
                await ProcessRealtimeAsync(
                    CoachCadence.Corner, _builder.BuildCorner((CornerEvent)domainEvent.Payload, Context()), sessionId, ct)
                    .ConfigureAwait(false);
                break;
            case DomainEventKind.Sector:
                await ProcessRealtimeAsync(
                    CoachCadence.Sector, _builder.BuildSector((SectorEvent)domainEvent.Payload, Context()), sessionId, ct)
                    .ConfigureAwait(false);
                break;
            case DomainEventKind.Lap:
                var lap = (LapEvent)domainEvent.Payload;
                _currentLap = lap.LapNumber;
                await ProcessRealtimeAsync(CoachCadence.Lap, _builder.BuildLap(lap, Context()), sessionId, ct)
                    .ConfigureAwait(false);
                break;
            case DomainEventKind.Session:
                await ProcessDebriefAsync(
                    _builder.BuildSession((SessionEvent)domainEvent.Payload, Context()), sessionId, ct)
                    .ConfigureAwait(false);
                break;
            default:
                break;
        }
    }

    private GoldSessionContext Context() => _ambient.SessionMetadata() with { LapNumber = _currentLap };

    private async Task ProcessRealtimeAsync<TEvent>(
        CoachCadence cadence, GoldArtifact<TEvent> gold, string sessionId, CancellationToken ct)
    {
        IGoldView view = GoldView.For(gold);
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(view, _coachOptions);
        RuleDecision decision = _ruleEngine.ShouldSpeak(subset, cadence, _ambient.LatestGate(), _budget);

        if (decision.Outcome == RuleOutcome.Silent)
        {
            _logger.LogDebug("Coach silent [{Reason}] for {Cadence}", decision.Reason, cadence);
            return;
        }

        CoachAction top = subset[0];
        RenderedAction topRendered = PhraseRenderer.Render(top, view);
        bool noPb = !gold.Session.HasReference;

        // TemplateOnly means the budget cap was hit (the only TemplateOnly outcome) — no LLM call, and the row
        // is tagged TemplateBudget so it is distinguishable from an ordinary quality fallback.
        bool overBudget = decision.Outcome == RuleOutcome.TemplateOnly;
        CoachTip tip = overBudget
            ? ComposeRealtimeTip(cadence, gold, top, topRendered, topRendered.PhraseRu, TipSource.TemplateBudget, null, noPb, sessionId)
            : await CompleteRealtimeAsync(cadence, gold, view, subset, top, topRendered, noPb, sessionId, ct).ConfigureAwait(false);

        await _sink.EmitTipAsync(tip, ct).ConfigureAwait(false);
        _ruleEngine.NoteTip(cadence, _clock.GetUtcNow());

        if (overBudget)
        {
            _logger.LogInformation("Coach budget cap hit for {Cadence} — emitted a template tip", cadence);
        }
        else
        {
            await RefreshBudgetAsync(sessionId, ct).ConfigureAwait(false);
        }
    }

    private async Task<CoachTip> CompleteRealtimeAsync<TEvent>(
        CoachCadence cadence, GoldArtifact<TEvent> gold, IGoldView view, IReadOnlyList<CoachAction> subset,
        CoachAction top, RenderedAction topRendered, bool noPb, string sessionId, CancellationToken ct)
    {
        LlmRequest request = _promptBuilder.Build(gold, cadence, subset);
        var ids = subset.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        int maxWords = RealtimeMaxWords(cadence);
        bool allowRetry = cadence != CoachCadence.Corner; // a corner retry would land after the next corner

        LlmResult result = await _llm.CompleteAsync(request, ct).ConfigureAwait(false);
        if (TryAcceptRealtime(result, ids, maxWords, out string actionId, out string phrase, out string? model))
        {
            return BuildChosenTip(cadence, gold, view, subset, actionId, phrase, model, noPb, sessionId);
        }

        if (allowRetry && IsRetryable(result))
        {
            LlmRequest retry = request with { SystemPrompt = request.SystemPrompt + "\n\n" + _retryReminder };
            LlmResult second = await _llm.CompleteAsync(retry, ct).ConfigureAwait(false);
            if (TryAcceptRealtime(second, ids, maxWords, out actionId, out phrase, out model))
            {
                return BuildChosenTip(cadence, gold, view, subset, actionId, phrase, model, noPb, sessionId);
            }
        }

        return ComposeRealtimeTip(cadence, gold, top, topRendered, topRendered.PhraseRu, TipSource.Template, null, noPb, sessionId);
    }

    private CoachTip BuildChosenTip<TEvent>(
        CoachCadence cadence, GoldArtifact<TEvent> gold, IGoldView view, IReadOnlyList<CoachAction> subset,
        string actionId, string phraseRu, string? providerModelId, bool noPb, string sessionId)
    {
        CoachAction chosen = subset.First(a => a.Id == actionId);
        RenderedAction rendered = PhraseRenderer.Render(chosen, view);
        return ComposeRealtimeTip(cadence, gold, chosen, rendered, phraseRu, TipSource.Llm, providerModelId, noPb, sessionId);
    }

    private CoachTip ComposeRealtimeTip<TEvent>(
        CoachCadence cadence, GoldArtifact<TEvent> gold, CoachAction action, RenderedAction rendered,
        string phraseRu, TipSource source, string? providerModelId, bool noPb, string sessionId)
    {
        (string? cornerId, string? cornerName, string? cornerNameShort, string? cornerNameSpoken) = CornerInfo(gold);
        return new CoachTip(
            SessionId: sessionId,
            Cadence: cadence,
            CornerId: cornerId,
            LapNumber: _currentLap,
            ActionId: action.Id,
            ActionLabelShort: rendered.ActionLabelShort,
            RenderedParam: rendered.RenderedParam.Length == 0 ? null : rendered.RenderedParam,
            Priority: action.Priority,
            Severity: _coachOptions.SeverityFor(action.Priority),
            PhraseRu: phraseRu,
            CornerName: cornerName,
            CornerNameShort: cornerNameShort,
            CornerNameSpokenRu: cornerNameSpoken,
            Source: source,
            NoPbYet: noPb,
            ProviderModelId: providerModelId,
            GeneratedAtUtc: _clock.GetUtcNow());
    }

    private (string? CornerId, string? Name, string? Short, string? Spoken) CornerInfo<TEvent>(GoldArtifact<TEvent> gold)
    {
        string trackId = gold.Session.TrackId;
        return gold.Event switch
        {
            GoldCornerEvent c when !string.IsNullOrWhiteSpace(c.CornerId) =>
                (c.CornerId, c.CornerName, _names.GetShort(trackId, c.CornerId), _names.GetSpokenRu(trackId, c.CornerId)),
            GoldCornerEvent c => (c.CornerId, c.CornerName, null, null),
            GoldSectorEvent s => (null, s.TopCorner, null, null),
            GoldLapEvent l => (null, l.TopCorner, null, null),
            _ => (null, null, null, null),
        };
    }

    private async Task ProcessDebriefAsync(GoldArtifact<GoldSessionPayload> gold, string sessionId, CancellationToken ct)
    {
        // The debrief always yields a real artifact (never an empty one): the LLM (offline → FakeProvider via
        // the router's Llm:Live switch), with a deterministic template fallback. No subset/quiet-zone gating —
        // it is the end-of-session summary.
        CoachTip tip = await CompleteDebriefAsync(gold, sessionId, ct).ConfigureAwait(false);

        await _sink.EmitTipAsync(tip, ct).ConfigureAwait(false);
        await RefreshBudgetAsync(sessionId, ct).ConfigureAwait(false);
        // The debrief is intentionally un-gated (terminal once-per-session summary), so it is not cooldown-tracked.
    }

    // Reads the current session + rolling-30-day spend into the cached budget the next ShouldSpeak checks.
    // Called after each LLM-bearing event (sparse), never per frame. A read failure keeps the prior snapshot.
    private async Task RefreshBudgetAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            CostSummary session = await _cost.GetSessionCostAsync(sessionId, ct).ConfigureAwait(false);
            RollingCost rolling = await _cost.GetRolling30DayCostAsync(ct).ConfigureAwait(false);
            _budget = new BudgetState((decimal)session.CostUsd, (decimal)rolling.CostUsd);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Coach budget refresh failed; keeping the previous budget snapshot");
        }
    }

    private async Task<CoachTip> CompleteDebriefAsync(GoldArtifact<GoldSessionPayload> gold, string sessionId, CancellationToken ct)
    {
        LlmRequest request = _promptBuilder.Build(gold, CoachCadence.Session, []);

        LlmResult result = await _llm.CompleteAsync(request, ct).ConfigureAwait(false);
        if (TryAcceptDebrief(result, out string json, out string? model))
        {
            return ComposeDebriefTip(gold, json, TipSource.Llm, model, sessionId);
        }

        if (IsRetryable(result))
        {
            LlmRequest retry = request with { SystemPrompt = request.SystemPrompt + "\n\n" + _retryReminder };
            LlmResult second = await _llm.CompleteAsync(retry, ct).ConfigureAwait(false);
            if (TryAcceptDebrief(second, out json, out model))
            {
                return ComposeDebriefTip(gold, json, TipSource.Llm, model, sessionId);
            }
        }

        return ComposeDebriefTip(gold, DebriefTemplate.BuildJson(gold, _coachOptions.MaxDebriefLosses), TipSource.Template, null, sessionId);
    }

    private CoachTip ComposeDebriefTip(
        GoldArtifact<GoldSessionPayload> gold, string debriefJson, TipSource source, string? providerModelId, string sessionId)
    {
        var priority = new CoachPriority(CoachPhase.Exit, int.MaxValue); // debrief is the least-urgent band
        (string topPriority, string? topLossesJson, string? setupHint) = ParseDebrief(debriefJson);
        return new CoachTip(
            SessionId: sessionId,
            Cadence: CoachCadence.Session,
            CornerId: null,
            LapNumber: null,
            ActionId: "debrief",
            ActionLabelShort: null,
            RenderedParam: null,
            Priority: priority,
            Severity: _coachOptions.SeverityFor(priority),
            PhraseRu: topPriority,
            CornerName: null,
            CornerNameShort: null,
            CornerNameSpokenRu: null,
            Source: source,
            NoPbYet: !gold.Session.HasReference,
            ProviderModelId: providerModelId,
            GeneratedAtUtc: _clock.GetUtcNow(),
            TopLossesJson: topLossesJson,
            SetupHint: setupHint);
    }

    private bool TryAcceptRealtime(
        LlmResult result, IReadOnlyCollection<string> ids, int maxWords,
        out string actionId, out string phraseRu, out string? providerModelId)
    {
        actionId = string.Empty;
        phraseRu = string.Empty;
        providerModelId = null;
        if (result is not LlmResult.Success success)
        {
            return false;
        }

        providerModelId = success.Info.ProviderModelId;
        return TipValidator.TryValidateRealtime(success.Json, ids, maxWords, out actionId, out phraseRu, out _);
    }

    private bool TryAcceptDebrief(LlmResult result, out string json, out string? providerModelId)
    {
        json = string.Empty;
        providerModelId = null;
        if (result is not LlmResult.Success success)
        {
            return false;
        }

        providerModelId = success.Info.ProviderModelId;
        if (TipValidator.TryValidateDebrief(success.Json, _coachOptions.MaxDebriefLosses, _coachOptions.DebriefMaxWords, out _, out _))
        {
            json = success.Json;
            return true;
        }

        return false;
    }

    // Retry only on a model-quality miss (a success that failed validation, or an explicit schema violation);
    // never on timeout / transport / auth / rate-limit / circuit-open — those are not fixed by re-asking.
    private static bool IsRetryable(LlmResult result) => result switch
    {
        LlmResult.Success => true,
        LlmResult.Failure(LlmFailure.SchemaViolation) => true,
        _ => false,
    };

    private int RealtimeMaxWords(CoachCadence cadence) => cadence switch
    {
        CoachCadence.Corner => _coachOptions.InCornerMaxWords,
        CoachCadence.Sector => _coachOptions.SectorMaxWords,
        CoachCadence.Lap => _coachOptions.LapMaxWords,
        _ => _coachOptions.InCornerMaxWords,
    };

    // Reads the debrief payload once: top_priority -> the spoken headline; top_losses (verbatim JSON array) and
    // setup_hint -> the structured columns persisted for the P6 debrief window. Both the validated LLM debrief
    // and the deterministic template fallback (DebriefTemplate.BuildJson) emit all three, so every persisted
    // debrief row is self-renderable regardless of source (coach_tips does not keep the Gold artifact).
    private static (string TopPriority, string? TopLossesJson, string? SetupHint) ParseDebrief(string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        string topPriority = root.TryGetProperty("top_priority", out JsonElement priority) && priority.ValueKind == JsonValueKind.String
            ? priority.GetString() ?? string.Empty
            : string.Empty;
        string? topLosses = root.TryGetProperty("top_losses", out JsonElement losses) && losses.ValueKind == JsonValueKind.Array
            ? losses.GetRawText()
            : null;
        string? setupHint = root.TryGetProperty("setup_hint", out JsonElement hint) && hint.ValueKind == JsonValueKind.String
            ? hint.GetString()
            : null;
        return (topPriority, topLosses, setupHint);
    }
}
