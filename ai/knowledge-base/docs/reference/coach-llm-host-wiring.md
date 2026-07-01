# Coach + LLM host wiring (PR-H)

Findings from wiring the Coach + LLM stack into the live host (Phase-3 close-out).

## One flag, in the router — not in Coach

`LlmOptions.Live` is the single fake-vs-real switch and it lives in `LlmRouter.Resolve`: while
`Live=false` **every** route resolves to the `OfflineProviderId`/`OfflineModelId` pair (`fake`/`fake/local`)
while keeping the route's own timeout/tokens/reasoning. Consequences:

- `CoachService` **always** calls `ILlmClient` — it has no provider/offline concept (LOCKED #1). The old
  `CoachServiceOptions.LlmLive` (Coach deciding whether to call the LLM) was deleted; template fallback now
  only means a real failure / validation miss / budget downgrade.
- Replay/CI produce real, **zero-cost** `llm_usage` rows with **no API key and no network** because the
  fake provider is configured with a zero rate. Going live = flip `Llm:Live` (settable via the settings
  store); no recompile, no per-route edit.
- `LlmStartupValidator` #1 must require a rate for the **offline pair** too — else `Live=false` hits a
  runtime rate-miss the cost meter swallows (dropped row).
- `AddLlm` calls `services.AddHttpClient()` unconditionally: a fake-only config has no named clients, but
  the provider-map factory still resolves `IHttpClientFactory`.

## Options registration: monitor-only for LLM, concrete for the rest

`LlmOptions` is registered as `IOptionsMonitor` **only** (no concrete singleton) so a settings write
re-binds without a restart. The bare-options consumers (`CoachOptions`, `RuleEngineOptions`,
`PromptOptions`, `RateCardOptions`, `CircuitBreakerOptions`) use the repo idiom
`GetSection().Get<T>() + EnsureValid() + AddSingleton(concrete)`. A capture-once concrete `LlmOptions`
singleton would defeat the re-bind, so it is deliberately absent.

## Settings re-bind before Build()

`Program` migrates the DB and opens `SqliteSettingsConfigurationSource` **before** `builder.Build()` (the
source reads the `settings` table at config-build time). It is inserted just **below** the `SIMCOACH_` env
source (the last source added) so a deliberate env override still wins over a stored row — preserving the
documented replay override loop. The same `ResolveDatabaseOptions` resolver feeds both the pre-build factory
and the DI factory, so every path opens one database.

## Load-bearing hosted stop order

Registration order (reverse = stop): SessionManager → McapRecorderService → **CoachService →
LiveCoachAmbientState** → ComputeService → IngestService. The Coach pair is slotted between the recorder and
ComputeService by `AddTelemetryPipeline` calling `AddCoachStack` at that point. CoachService **must** stop
after ComputeService completes the domain-event fan-out, because CoachService drains it to completion to emit
the final debrief; register it before ComputeService → it stops after.

## Testing the drain: await ExecuteTask, don't RunAsync

The Coach replay e2e drives the **manual** harness (start services → run ingest → await each consumer's
`ExecuteTask` → stop) rather than `host.RunAsync()`. `IngestService` calls
`IHostApplicationLifetime.StopApplication()` when the replay source drains; under `RunAsync` that begins host
shutdown, whose cancellation aborts `ComputeService` mid-drain (0 laps) and trips `CoachService`'s startup
budget-seed (`RefreshBudgetAsync` re-throws `OperationCanceledException`). Awaiting `ExecuteTask` keeps the
drain off the cancellation path and is deterministic. The host *composition* (services resolve,
`ValidateOnStart` throws on a bad config) is smoke-tested separately in `SimCoach.App.Tests` against the real
shipped `appsettings.json`.

## session_id seam

`SqliteCostMeter` stamps `llm_usage.session_id` from `ISessionIdProvider`, bridged at the App edge over the
producer-owned `SessionContext` (mirrors `ITrackLengthProvider`/`ICarClassProvider`) so the provider-agnostic
LLM library never reaches into the pipeline. `llm_usage.session_id` is an FK to `sessions(id)`: stamping a
non-null id requires the session row to exist first (it does — SessionManager persists on frame #1, long
before any tip).
