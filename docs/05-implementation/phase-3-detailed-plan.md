# Detailed Plan — Phase 3 (Coach Engine + LLM)

Expands `implementation-plan.md` Phase 3 into ordered, testable steps.
Status legend: `[ ]` todo, `[x]` done.

Phase 2 closed (C1–C9). Every `CornerEvent`/`SectorEvent`/`LapEvent`/`SessionEvent` is now emitted
deterministically over the lossless `DomainEventFanOut`, keyed by `track_id`/`car_id`/`weather_bucket`.
Phase 3 consumes those events to produce coaching tips.

This plan folds in the validated 2026-06-27 amendment pass: blockers **B1** (add the missing compute
kernels instead of deleting the actions that need them), **B2** (`aggregated_losses` data source), **B3**
(enumerated `ValidateOnStart` checklist); majors **M1–M7**; minors **m1–m5**; the owner product decisions
(tyre/brake-temp driving advice **in** P3; engine-map/ABS/TC data plumbed but advice **deferred**; pit
advisor *seam reserved*, delivery deferred); and the UI contracts the mockups prove the spine must expose
now (`CoachTip` DTO, `ICoachTipSink`, cost-query API, settings store, tip/debrief persistence) — see
`docs/03-functional/ui-client-requirements.md`. **Phase 3 designs those contracts; it does not build any
Avalonia view.**

---

## Goal

From live or replayed domain events, build a deterministic Gold-tier artifact per cadence, gate it
through quiet-zone rules, filter the bounded `ActionRegistry` to the *valid* action subset for the
situation, prompt a **provider-agnostic** LLM for a short structured Russian phrase (`action_id` +
`phrase_ru`), validate the response, fall back to a baked template on any failure, and route the tip
to an `ICoachTipSink` (console/log in P3) — persisting every tip and every LLM call's cost. All of it
runs **wired into the live App host** and is **fully offline-testable** on macOS via a `FakeProvider`
and golden fixtures; no live network call ever runs in CI.

To make those tips *correct* and the debrief *real*, Phase 3 also extends the compute layer: it **adds**
the tip-quality kernels the canonical registry references but Phase 2 never computed — wheelspin,
brake-overlap-steer, steering-jitter, and tyre/brake-temp abuse (B1) — and an `internal sealed`
`SessionLossAccumulator` inside `ComputeService` that turns the per-corner event stream into
`aggregated_losses` on `SessionEvent` (B2), the data source the debrief is otherwise missing.

Covers `FR-014` (no-reference handling), `FR-021..024` (the consumed domain events), `FR-030..037`
(the coaching engine), `FR-060/061` (debrief artifact + model), and `FR-072` (cost rollup).

**Explicitly deferred:** Voice/TTS (P4), the Avalonia overlay sink (P5), and the post-session debrief
*delivery* path including LLM **streaming** (P6 — the streaming seam is declared now but not built).
Phase 3 ships only the buffered `CompleteAsync` path for the three real-time cadences and the
debrief artifact/schema shape; the debrief LLM call may be wired behind the disabled live-LLM flag.

**Reserved, not built (owner decisions):** the **pit/strategy advisor** — a new `CoachCadence.Strategy`
enum value, a strategy quiet-zone, and the frame→Gold plumbing of fuel/tyre-wear/temps/pit-state/TC/ABS/
engine-map are *declared and plumbed* this phase, but **no Strategy tip is ever emitted in the MVP** (it
is template-first / LLM-optional, timed on the main straight / pit-window approach with ~1 lap lead, and
deferred to a later race-craft phase). Engine-map/ABS/TC **advice** actions are likewise reserved as
future registry entries — the data lands now, the actions do not.

## Design decisions (taken before this plan)

| Decision | Rationale |
|---|---|
| **LLM is provider-agnostic behind `ILlmClient`; OpenRouter is one leaf** | LOCKED #1. Coach speaks an **opaque `string RouteKey`**, never a provider model string and never a racing taxonomy that leaks into the LLM lib. A `LlmRouter : ILlmClient` resolves `RouteKey → (providerId, modelId, knobs)` from `IOptions`; provider quirks (auth, base URL, structured-output strategy, SSE shape, usage field names, error envelopes) live *only* inside Ring-2 adapters. Swapping OpenRouter→Gemini-direct/DeepSeek-direct/Anthropic/local-fake — or adding a brand-new provider — is a keyed registration + a `Routes`/`Providers` config edit, **no contract edit and no recompile** (provider id and route key are open strings, not closed enums). No OpenRouter-shaped type ever crosses into Coach. The mockup's hard-coded "OpenRouter / DeepSeek V3.2 / Gemini 2.5 Flash" copy is a **UI label to neutralise** ("LLM API-ключ / провайдер"), never a seam assumption. |
| **Real OpenRouter HTTP + CostMeter + CircuitBreaker ship this phase, behind a disabled live flag** | LOCKED #2. The code lands and is unit-tested through a **mocked `HttpMessageHandler`** (golden request/response fixtures); the default registered route resolves to `FakeProvider` so CI and replay need no API key and make no network call. A single `Llm:Live` flag (off by default) flips the router to real providers. |
| **Coach is wired into the live host, not dead-until-wired** | LOCKED #3. `CoachService` (a `BackgroundService`) subscribes to `DomainEventFanOut` **in its constructor** (mirrors `ComputeService`), runs GoldArtifactBuilder → RuleEngine → PromptBuilder → `ILlmClient` → validate/fallback → `ICoachTipSink`. Demonstrable on a replay session out of the box (against `FakeProvider`). |
| **Tips route through a single `ICoachTipSink`; the `CoachTip` DTO carries everything the overlay card renders** | LOCKED #4. P3 ships `ConsoleTipSink` (structured log + persist). Voice (P4) and Overlay (P5) implement the **same** interface; Coach never knows which sink is attached. The overlay coach card is the canonical proof of the DTO field set: `phrase_ru` + an `ActionLabelShort · RenderedParam` chip (`brake_later · +4м`) + a `cadence · severity` chip (`corner · high`) + a **full** corner name (`Eau Rouge`, for debrief/log surfaces) + a **short** form (`О-Руж`) + a **spoken** RU form (P4) + `ProviderModelId` + `NoPbYet` + timestamp. Those fields (`ActionLabelShort`, `RenderedParam`, total-order `Priority` **and** the derived `CoachSeverity` band, `CornerName`/`CornerNameShort`/`CornerNameSpokenRu`) are added to `CoachTip` now so P4/P5 don't force a re-architecture. The `ActionLabelShort` is an **authored** registry label (not a `_by_meters` trim) and `CoachSeverity` is the deterministic `Priority`→band projection (not a view-side heuristic). |
| **Prompts are config-driven, versioned resources selected *per cadence*** | LOCKED #5. System + few-shot templates are embedded, version-named resources (`coach.system.v1.ru.txt`, `coach.system.debrief.v1.ru.txt`, …); `IOptions<PromptOptions>` selects the version **and the prompt+few-shot set per `CoachCadence`** (M4), and may override with an external file path — swappable without recompile. |
| **The ActionRegistry gates what the LLM may select; its `when` fields must exist in compute** | Registry is an embedded JSON resource (data, not code). `when` clauses are typed predicates (no expression strings, no `dynamic`). PromptBuilder computes the valid subset deterministically in C#; only the surviving 0–5 actions reach the prompt **and** the output-schema `enum`. Out-of-spec `action_id` → schema fail → template fallback. **B1 resolution:** the canonical registry references compute fields Phase 2 never produced (wheelspin, brake-overlap-steer, steering-jitter, tyre/brake-temp), so the fail-fast field-vs-Gold validator (B3 #4) would crash host startup. The owner decision is to **add the kernels, not delete the actions** — the raw data already exists (proto `tyre_temp_c`/`brake_temp_c`/`wheel_slip`/`tyre_wear_pct`; ACC SHM `WheelSlip`/`SlipRatio`/`BrakeTemp`/`TyreTempI/M/O`/`PadLife`). See D0. |
| **Action priority is a TOTAL ORDER, not a 3-value enum** | M5. A `corner > … > exit` causal-phase rank plus metric magnitude gives every action a deterministic integer key, so `Take(MaxActionsInMenu)` and the golden tests are stable, and a **root cause beats its symptom** (brake-phase action outranks an exit-phase consequence). The old `{high,medium,low}` enum left ties that made `Take(5)` and golden ordering non-deterministic. The overlay's coarse "high"/"средний"/"низкий" **chip is a separate `CoachSeverity` band**, derived deterministically from the integer key via config `CoachOptions.SeverityBands` — so the chip has a stable source without re-introducing the deleted enum into ordering (the integer Rank stays the sole sort key). |
| **One canonical cadence enum in Coach; `Strategy` reserved; the LLM lib stays cadence-blind** | `CoachCadence { Corner, Sector, Lap, Session, Strategy }` (owned by `SimCoach.Coach`) is the single taxonomy. The Gold/registry `cadence` strings map 1:1. Coach maps `CoachCadence → RouteKey` via `CoachOptions` (default `corner`/`sector`/`lap`/`debrief`/`strategy`); the LLM contract sees only the route-key string. **`Strategy` is reserved now** (enum value + route mapping + a dedicated strategy quiet-zone) for the deferred pit advisor; **no Strategy tip is emitted in MVP.** |
| **Compute owns loss aggregation, not the LLM (B2)** | An `internal sealed SessionLossAccumulator` inside `ComputeService` accumulates `CornerEvent`/`LapEvent` losses and emits `aggregated_losses[]{corner_id, total_loss_ms, avg_loss_ms, sample_count, dominant_reason}` on `SessionEvent` (a new appended `AggregatedLoss` proto message). `dominant_reason` is sourced from the appended `CornerEvent.reason` field (the value `CornerEventBuilder` already computes); **no `corner_name` on the event** — names stay out of compute per ADR-0010 and are resolved at the Coach layer by `GoldArtifactBuilder` via `CornerNameMap`, so PR-B does not depend on PR-C. Without this accumulator `SessionEvent` carries no loss data and the debrief prose (`FR-060`) is **contradicted** — the "Теряешь 0.6 с на выходах из 7/8" panel has no source. Mutation is isolated to the `internal sealed` collector (records/`IReadOnly` on the public surface). `aggregated_losses` is **bounded** (`maxItems` in the schema + a post-parse cap, m2). |
| **Model defaults are config data; debrief = Claude Sonnet 4.6, real-time = Gemini 2.5 Flash-Lite; CostMeter reads rates from config** | M1/M2/m1. **Debrief default pins ONE id: `anthropic/claude-sonnet-4.6`** (canonical Anthropic id `claude-sonnet-4-6`, **$3/$15 per 1M**, 1M ctx) with **Reasoning=Low** (adaptive thinking) — owner default at ~1.8¢/session; `anthropic/claude-haiku-4.5` (`claude-haiku-4-5`, **$1/$5**, ~0.6¢/session) is the documented cheaper middle ground. **DeepSeek is GATED OFF** until vLLM #41132 (thinking+JSON-schema corruption) is verified fixed (M2); the stale `deepseek-chat-v3.2`-vs-V4 ambiguity is therefore moot for the default. **Real-time default `google/gemini-2.5-flash-lite`** ($0.10/$0.40), eval-gated; the older `google/gemini-2.5-flash` is the fallback, **not** a "confirmed default" (m1). The Gemini 3.x generations (3 Flash / 3.1 Flash-Lite / 3.5 Flash) exist mid-2026; on all of them thinking **cannot be fully disabled** (`thinking_level: minimal` still reasons per ai.google.dev), so against the hard 2000 ms **buffered** corner budget 2.5 Flash-Lite is preferred for its **deterministic no-thinking latency** (TTFT ~0.26 s, `thinking_budget=0`), and the task is reasoning-insensitive (selection is a pre-filtered C# subset) so 3.x's quality gains buy little here. **`google/gemini-3.1-flash-lite` ($0.25/$1.50; ~3× cost but still ~$0.014/session, negligible) is the named eval-gated UPGRADE candidate** — promote only if the RU eval (m5) shows 2.5 Flash-Lite's Russian is sub-par **and** `thinking_level: minimal` latency still fits the budget; 3.5 Flash is overkill for an ≤8-word phrase. (The earlier "thinking-first / blows the timeout" framing was imprecise — at `minimal` the 3.x models are fast; the real reasons are latency determinism + reasoning-insensitivity + cost.) CostMeter never hard-codes a price; the exact route id is config, validated at composition. |
| **Real-time cadences: no streaming, reasoning OFF; debrief reasoning LOW** | The whole structured-JSON response must be parsed before any `action_id` is actionable, so streaming buys nothing at real-time cadence, and a half-parsed `action_id` is unvalidatable. Action selection is classification over a pre-validated subset — all metric reasoning already happened deterministically in compute — so "thinking" is pure latency/cost real-time. **The "no accuracy upside" claim is design-asserted, gated by the RU eval, not measured (m3).** `Stream`/`ReasoningEffort` are provider-neutral **route** knobs (config). Debrief runs Reasoning=Low (Sonnet 4.6 adaptive thinking) within its 8000 ms budget. The streaming debrief seam is declared (`StreamAsync`) but only consumed in P6. |
| **UI read/persistence/settings contracts are designed in P3, not built** | The mockups (7 screens) bind to spine contracts. P3 **implements** the `CoachTip` DTO + `ICoachTipSink`, the `coach_tips`/`llm_usage` columns, the `ISettingsStore` over the existing `settings` table, and the `ICostQueryRepository` over `llm_usage`; it **declares** (signatures + reserved nullable columns) the `IReferenceQueryRepository`, `ISessionHistoryRepository`, and the `debrief` row shape that P6/P7 fill. The live intra-lap delta / per-sector-delta / speed-trace **reads** are noted as P5 compute extensions and explicitly do **not** block P3. The only Phase-3 *call* among them is M7 — add `normalizedCarPosition` to the gate snapshot, taken below. |
| **M7: `normalizedCarPosition` is added to the gate snapshot (not deferred)** | The apex-window / straight / user-quiet-zone gates need lap position, and the dashboard `ПОЗИЦИЯ НА КРУГЕ` panel needs it too. Rather than let those gates silently no-op, the gate-only frame snapshot gains `normalized_car_position` (already a frame field — trivial) plus a corner-phase marker derived from the active corner window. Decision recorded so the gates are real, not stubs. |
| **New `coach_tips` table + `llm_usage` extension via new numbered migrations, numbered in MERGE order** | `002_llm_usage_cost.sql` ships in PR-F (adds `provider`, `cached_input_tokens` — **not** `model_id`, which already exists in `001`); `003_coach_tips.sql` ships in **PR-H** with `rendered_param` + `priority` columns (so the tip log / debrief re-render the `+4м` chip and ordering offline). Numbering follows merge order so an incrementally-upgraded DB never silently skips the later one. `001_initial.sql` is never modified (migrator owns the transaction). |

Build-order dependency: **D0 → D1 → {D2a ∥ D3} → {D2b/D4} → D5 → D6 → D7 → D8 → D9**, where the LLM-seam
contract **PR-A** lands first of all (it is the only edit to existing `ILlmClient` code). D3 precedes
D2b/D4 because `PromptBuilder` (D4) consumes D3's Gold artifact; D2a (corner-name) parallels D3. D0
(compute kernels + `SessionLossAccumulator` + strategy data plumb) is a compute-layer prerequisite: D3's
Gold builders read its new `CornerEvent`/`LapEvent`/`SessionEvent` fields, and D1's registry `when` clauses
reference them (so the B3 #4 validator passes).

Runtime dependency (separate from build order): D3's `GoldArtifactBuilder` consumes D2a's `CornerNameMap`
(to resolve `aggregated_losses` `corner_id`→`corner_name` at the Coach layer, ADR-0010) — so D3 follows
D2a/D1, not D0; D0 stays name-free. D4's `PromptBuilder` consumes D1's `ActionRegistry` (valid-subset
filter) + `CornerNameMap` + D3's Gold artifact. D8's `CoachService` orchestrates D3 → D7(RuleEngine) →
D4 → D5(`ILlmClient`) → cadence-aware validate/fallback(D1 templates / `DebriefTemplate`) → `ICoachTipSink`,
recording cost via D6 and tripping D5's per-provider breaker.

---

## Architecture

### The provider-agnostic LLM seam (three rings + an explicit decorator chain)

```
SimCoach.Coach ──depends-on──► ILlmClient            RING 0  (agnostic contract; SimCoach.LLM)
                                   ▲                          Coach passes an opaque string RouteKey
   ILlmClient is composed as a decorator chain (one responsibility each):
   LlmRouter ─► per-provider CircuitBreaker decorator ─► CostMeter decorator ─► ILlmProvider
     │ resolves RouteKey→(providerId, modelId, knobs)   RING 1  (agnostic decorators)
     ▼
   internal ILlmProvider adapters                     RING 2  (provider-specific; SimCoach.LLM.Providers)
   ├ OpenRouterProvider   ├ GeminiProvider  ├ DeepSeekProvider (config-gated OFF, M2)
   ├ AnthropicProvider    ├ FakeProvider (tests + CI default)
        each owns: BaseUrl + AuthHandler · ISseDecoder · usage→LlmUsage map · HTTP→LlmFailure
        SCHEMA strategy is NOT owned per gateway-provider: ISchemaTranslator is selected by the resolved
        modelId/FAMILY (json_schema | responseSchema | json_object+inject | forced-tool), because in P3 the
        single OpenRouterProvider fronts BOTH Gemini (real-time) and Anthropic Sonnet (debrief), which need
        different schema shapes (see D5).
```

Because the one shipped `OpenRouterProvider` fronts two upstream families, it is registered under **two
distinct `providerId`s** — `openrouter-google` (real-time) and `openrouter-anthropic` (debrief) — both
backed by the same adapter type. That keeps the per-`providerId` circuit breaker **isolated per upstream**
(a Gemini failure storm must not open the Anthropic debrief breaker), which a single shared `providerId`
would violate (see D7).

`LlmRouter` is a single-responsibility orchestrator: it resolves the route and delegates to a chain it
does **not** itself implement — the `CircuitBreaker` and `CostMeter` are separate decorator types
wrapping the chosen `ILlmProvider`. Coach references **only** `ILlmClient` + `LlmRequest`/`LlmResult`;
`RouteKey` and `ProviderId` are opaque strings, so zero provider types cross the boundary and **adding a
new provider (even one not named here) needs no edit to Coach or the Ring-0 contract** — only a keyed
registration and config. The OpenRouter-style `ModelId` slug (`anthropic/claude-sonnet-4.6`, dotted) and
the canonical Anthropic id (`claude-sonnet-4-6`, hyphenated) are both opaque to Ring 0 and resolved per
provider — **the seam is not locked to OpenRouter.**

### Coach engine pipeline (per domain event)

```
DomainEventFanOut.Subscribe("coach")            (in CoachService ctor — never miss opening events)
TelemetryFanOut.Subscribe("coach-gate")         (ctor — latest-frame SNAPSHOT only; gate input, never serialized)
        │  CornerEvent | SectorEvent | LapEvent | SessionEvent      (Strategy: reserved, never emitted in MVP)
        ▼
GoldArtifactBuilder.Build(event, ctx)           deterministic Gold JSON (derived scalars only)
        │
        ▼
PromptBuilder.ValidSubset(gold)                 registry.where(cadence ∧ requiresRef ∧ when…) → 0..N
        │
        ▼
RuleEngine.ShouldSpeak(gold, subset, frame, clock)   quiet zones: empty subset · cooldown · workload ·
        │  Silent ──────────────► (record reason, emit nothing)   straight · apex-window · recent-
        │                                                          contact/off-track · user quiet zone ·
        ▼ Speak                                                    session-not-green · strategy-zone ·
CostMeter.OverBudget? ── yes ─► TemplateOnly(subset.Top)  (no LLM)  budget · priority-floor
        │ no
        ▼
PromptBuilder.Build(gold, subset)               system+few-shot (per cadence, M4) + Gold user msg + schema(enum=subset)
        │  LlmRequest{ RouteKey, System, User, JsonSchema, SchemaName }   (knobs resolved from route config)
        ▼
ILlmClient.CompleteAsync  ──► LlmResult          (LlmRouter → CircuitBreaker → CostMeter → provider)
        │
        ▼
validate (cadence-aware) { real-time: action_id ∈ subset, wordCount(phrase_ru) ≤ max, non-empty
        │                     debrief: top_losses ≤ maxItems, top_priority non-empty, Σwords ≤ 200 }
        │  ok ───────────────► CoachTip(source=llm)
        │  bad/Failure ─► (sector/lap/debrief) retry once ─► still bad ─► TEMPLATE FALLBACK
        │                  (real-time: subset.Top · debrief: deterministic DebriefTemplate from aggregated_losses)
        │                  (corner cadence: NO retry — would land after the next corner — straight to template)
        ▼
ICoachTipSink.EmitTipAsync(CoachTip)            ConsoleTipSink (P3) · Voice (P4) · Overlay (P5)
        │
        ▼
CoachTipRepository.Insert  +  CostMeter.RecordAsync          (coach_tips + llm_usage rows)
```

The `TelemetryFanOut` subscription supplies **only** a lock-free *latest-frame snapshot* — a handful of
scalars (brake, steer, steer-rate, speed, off-track/contact flags, **and `normalized_car_position` +
corner-phase marker per M7**) for the workload/straight/apex/user-zone gates. It is never assembled into a
Gold artifact and never serialized to the LLM, so "only Gold-tier JSON leaves the machine" still holds.
For corner-cadence tips (emitted at corner exit) the workload/straight gate is largely moot; the snapshot
mainly serves the sector/lap gates and the (reserved) strategy zone.

### Module map (where each new type lives)

| Artifact | Project / path | Form |
|---|---|---|
| Tip-quality kernels (wheelspin / brake-overlap-steer / steering-jitter / tyre+brake-temp abuse) | `SimCoach.Pipeline/Kernels/` | pure functions, one per file |
| New `CornerEvent` (`wheelspin_score`/`brake_overlap_steer_pct`/`steering_jitter`/`reason`) + `LapEvent` temp-summary + `SessionEvent` (M3) fields + new `AggregatedLoss` message | `Contracts/Schemas/telemetry.proto` | **append-only proto** (protoc regen) |
| `SessionLossAccumulator` (B2) — fills the new fields/`aggregated_losses` | `SimCoach.Reference/Compute/` (+ `CornerEventBuilder`/`ComputeSession`) | `internal sealed` collector |
| Strategy/pit Gold-input plumb (fuel/wear/temps/pit-state/TC/ABS/engine-map) | `Contracts/Schemas/telemetry.proto`, `Adapters.ACC/AccFrameMapper.cs` | append-only proto + mapper lines |
| `GoldArtifact*` records + `GoldArtifactBuilder` (per-cadence methods) | `SimCoach.Coach/Gold/` | records, one type per file |
| `CoachCadence` (canonical enum incl. reserved `Strategy`) | `SimCoach.Coach/CoachCadence.cs` | enum |
| `actionRegistry.json` + loader + `WhenClause`/`ClauseOp` evaluator + total-order `Priority` | `SimCoach.Coach/Data/`, `SimCoach.Coach/Actions/` | embedded JSON + records |
| `CornerNameMap` (exists) + positional fallback + short + spoken-RU forms | `SimCoach.Coach/CornerNameMap.cs`, `SimCoach.Coach/Resources/CoachStrings.ru.resx` | data + resx |
| Output schemas (real-time, debrief), built per request from the subset | `SimCoach.Coach/Schema/` | generated JSON string |
| `PromptBuilder` (provider-neutral → `LlmRequest`; per-cadence prompt selection) | `SimCoach.Coach/PromptBuilder.cs` | one public type |
| System/few-shot templates, versioned, per cadence (incl. debrief) | `SimCoach.LLM/Prompts/*.v1.ru.*` | embedded, `IOptions` override |
| `RuleEngine` + `RuleEngineOptions` (`TimeProvider`-injected, frame snapshot, strategy zone) | `SimCoach.Coach/RuleEngine/` | pure, `IOptions` |
| `ICoachTipSink` + `ConsoleTipSink` + `CoachTip` record (full UI field set) | `SimCoach.Coach/` | abstraction (P4/P5 reuse) |
| `CoachService` (`BackgroundService`) + `CoachOptions` | `SimCoach.Coach/` | hosted service |
| `ILlmClient` contract (revised) + `LlmRouter` + `ReasoningEffort` etc. | `SimCoach.LLM/` | Ring 0/1 |
| `ILlmProvider`, `OpenRouterProvider`, `FakeProvider`, `ISchemaTranslator`, `ISseDecoder` | `SimCoach.LLM/Providers/` | Ring 2, `internal` |
| `LlmOptions`/`RouteOptions`/`ProviderOptions`/`ModelRate`/`CircuitBreakerOptions` + `ValidateOnStart` | `SimCoach.LLM/` | records, `IOptions` |
| `ICircuitBreakerRegistry` + breaker (per `providerId` string; distinct ids per upstream family) | `SimCoach.LLM/` | in-memory |
| `ISchemaTranslator` selection keyed on resolved `modelId`/family (not gateway `providerId`) | `SimCoach.LLM/Providers/` | strategy map |
| `ICostMeter` + `SqliteCostMeter`; `ICostQueryRepository` + impl | `SimCoach.LLM`/`SimCoach.Storage` | persists/queries `llm_usage` |
| `ISettingsStore` (over existing `settings`); `IReferenceQueryRepository`/`ISessionHistoryRepository` (declared) | `SimCoach.Storage/Repositories/` | Dapper |
| `CoachTipRepository` | `SimCoach.Storage/Repositories/CoachTipRepository.cs` | Dapper |
| `llm_usage` cost columns / `coach_tips` table | `…/Schema/002_llm_usage_cost.sql`, `003_coach_tips.sql` | new numbered migrations (merge order) |
| `AddCoaching` / `AddLlm` composition | `SimCoach.App/CoachComposition.cs` (+ `TelemetryComposition`/`Program.cs` edits) | DI wiring |

### Wiring into the live host (load-bearing stop order + drain-to-completion)

Hosted-service `StopAsync` runs in **reverse** of registration. `SessionManager` must stop **last**
(it finalizes the session row from persisted `laps`), and `CoachService` must drain its tips to the
sink **before** the session closes but **after** compute has produced the events it consumes. Insert
`CoachService` between `ComputeService` and `McapRecorderService`:

```
AddHostedService<SessionManager>()        // registered 1st → stops LAST  (finalizes session row)
AddHostedService<McapRecorderService>()   // stops 4th
AddHostedService<CoachService>()          // stops 3rd  (NEW — drains tips before session finalize)
AddHostedService<ComputeService>()        // stops 2nd  (produces domain events + aggregated_losses)
AddHostedService<IngestService>()         // registered last → stops FIRST (producer)
```

`CoachService` subscribes to `DomainEventFanOut` (and the gate-only `TelemetryFanOut`) in its
constructor, so it cannot miss the opening `SessionEvent`/early corners. **On shutdown it drains its
`DomainEventSubscription` to channel *completion*, not on `stoppingToken`** — mirroring
`ComputeService`, which emits `SessionEvent` (now carrying `aggregated_losses`) and completes the
fan-out even under cancellation. A `BackgroundService` that bailed on `ReadAllAsync(stoppingToken)`
would drop exactly the buffered tail (including the final `SessionEvent`-derived debrief tip) it must
process; instead `ExecuteAsync` reads to completion, bounded by `StopAsync`'s shutdown timeout. The
gate-only frame subscription needs no drain (frames stop at `IngestService`, which stops first). All
Coach/LLM options call `EnsureValid()`/`ValidateOnStart` (the B3 checklist) at composition, failing fast.

---

## Key C# contract (Ring 0 — revised `ILlmClient`)

The existing `ILlmClient` (`ModelId` string, `Failure(string)`, two bare int token fields) leaks
provider shape and cannot drive a circuit breaker or a config-priced CostMeter. The contract is revised
so it carries an **opaque route key** (no racing taxonomy) and an **open string provider id** (no closed
enum). These trunk-safe edits land first (PR-A):

```csharp
public interface ILlmClient
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
    IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, CancellationToken ct); // declared; consumed in P6
}

// Opaque route key (e.g. "corner"/"sector"/"lap"/"debrief"/"strategy"); the router maps it to
// (providerId, modelId, MaxOutputTokens, Timeout, Reasoning, Stream) from LlmOptions.Routes.
// The LLM library knows nothing about racing cadences.
public sealed record LlmRequest(
    string RouteKey, string SystemPrompt, string UserPrompt,
    string JsonSchema, string SchemaName);              // SchemaName: OpenRouter json_schema.name / Anthropic tool name

public enum ReasoningEffort { Off, Low }                // route config. OpenRouter: Off→reasoning:{enabled:false},
                                                        // Low→reasoning:{effort:"low"}. A future DIRECT Anthropic
                                                        // adapter maps Low→thinking:{type:"adaptive"}+effort — NEVER
                                                        // the deprecated budget_tokens/thinking_budget (removed on Opus 4.7+).

public abstract record LlmResult
{
    public sealed record Success(string Json, LlmUsage Usage, LlmCallInfo Info) : LlmResult;
    public sealed record Failure(LlmFailure Error) : LlmResult;   // ← payload named Error (CS0542 forbids a member matching the enclosing record name); structured, drives breaker + retry decision
}

public sealed record LlmUsage(int InputTokens, int OutputTokens, int CachedInputTokens = 0, int ReasoningTokens = 0);
public sealed record LlmCallInfo(string ProviderId, string ProviderModelId, TimeSpan Latency, string? FinishReason);

public abstract record LlmFailure(string Message)
{
    public sealed record Timeout(string Message) : LlmFailure(Message);
    public sealed record RateLimited(string Message, TimeSpan? RetryAfter) : LlmFailure(Message);
    public sealed record SchemaViolation(string Message, string RawText) : LlmFailure(Message); // model-quality → retry/template, NOT breaker
    public sealed record Auth(string Message) : LlmFailure(Message);
    public sealed record ServerError(string Message, int StatusCode) : LlmFailure(Message);
    public sealed record Transport(string Message) : LlmFailure(Message);
    public sealed record CircuitOpen(string Message) : LlmFailure(Message);
}

// Coach side — one canonical cadence enum, mapped to a RouteKey string via CoachOptions.
// Strategy is RESERVED (pit advisor): enum value + route mapping + quiet-zone exist; no tip emitted in MVP.
public enum CoachCadence { Corner, Sector, Lap, Session, Strategy }  // Corner→"corner" … Session→"debrief", Strategy→"strategy" (config)

public interface ICoachTipSink { Task EmitTipAsync(CoachTip tip, CancellationToken ct); }  // non-blocking — cannot stall the pipeline

// Total order (M5): lower Rank == higher urgency. Built from causal phase (brake>entry>apex>exit)
// then metric magnitude, so Take(N) + golden ordering are deterministic and root cause beats symptom.
// Rank drives ORDERING (Take(N)/tip-log); the overlay's coarse "high" CHIP is the separate Severity below.
public readonly record struct CoachPriority(int Rank);

// Discrete DISPLAY band the overlay chip renders ("corner · high"). Derived DETERMINISTICALLY in C# at
// build time from Priority (causal phase + Rank thresholds in CoachOptions.SeverityBands) — never a
// view-side heuristic, never the magic-number bucketing the hard rules forbid. Kept SEPARATE from the
// total-order Rank so ordering stays integer-keyed while the chip has a stable, config-driven source.
public enum CoachSeverity { Low, Medium, High }

public sealed record CoachTip(
    string SessionId, CoachCadence Cadence, string? CornerId, int? LapNumber,
    string ActionId, string? ActionLabelShort,         // NEW (UI): authored short chip label ("brake_later"), not a trimmed id
    string? RenderedParam,                              // NEW (UI): the "+4м" chip value(s)
    CoachPriority Priority, CoachSeverity Severity,     // NEW (UI/M5): Rank = order; Severity = the "high" chip
    string PhraseRu,
    string? CornerName,                                 // NEW (UI): full canonical form ("Eau Rouge") for debrief/log surfaces
    string? CornerNameShort,                            // NEW (UI): "О-Руж" slim-overlay display form
    string? CornerNameSpokenRu,                         // NEW (UI/m4, P4 consumes): strip "(N)", RU ordinal
    TipSource Source, bool NoPbYet,
    string? ProviderModelId, DateTimeOffset GeneratedAtUtc);
public enum TipSource { Llm, Template }
// Tip DISMISSAL/TTL is owned by the consuming sink/view (a P5 overlay constant), NOT the DTO — recorded
// here so P5 does not churn this shared record. EmitTipAsync is non-blocking fire-and-forget.
```

`NoPbYet` flags a tip generated with no reference for the triple (Gold `has_reference=false`) so the sink
can label it "no PB yet" per `FR-014`. `CornerName`/`CornerNameShort`/`CornerNameSpokenRu` derive at emit
time from `CornerNameMap` (RU `.resx`) so no surface re-resolves a name from `CornerId`; the reference-free
M6 actions (`ease_understeer`/`settle_oversteer`) are what let the overlay still render a tip while
`NoPbYet=true`. `ActionLabelShort` is the **authored** short chip label carried on each registry entry (the
mockup chip reads `brake_later`, the canonical id is `brake_later_by_meters`) — the chip renders the label,
never a view-side `_by_meters` trim. `Severity` is the deterministic `Priority`→band projection (above);
`Priority.Rank` remains the sole sort key for `Take(N)` and the tip log.

### UI query contracts designed in P3 (implement / declare per §note)

```csharp
// Cost — implemented in P3 over llm_usage (Screen 06 / Screen 04 estimates / Screen 02 status bar).
public interface ICostQueryRepository
{
    Task<CostSummary>                 GetSessionCostAsync(string sessionId, CancellationToken ct);
    Task<RollingCost>                 GetRolling30DayCostAsync(CancellationToken ct);
    Task<IReadOnlyList<CostByDay>>    GetCostByDayAsync(int days, CancellationToken ct);
    Task<IReadOnlyList<CostByRoute>>  GetCostByRouteAsync(DateTimeOffset fromUtc, CancellationToken ct); // per cadence+provider+model
}
// CostByRoute { RouteKey, ProviderId, ModelId, CallCount, InputTokens, OutputTokens, CachedInputTokens, CostUsd }

// FORWARD cost estimate — Screen 04 renders "~$0.002 / круг" / "~$0.01 / сессия" next to a model the user
// is about to SWITCH TO (never used yet → historical spend can't price it). Prices it from the config
// rate-card (LlmOptions.Providers[…].Rates[modelId], validated by ValidateOnStart #1) × a typical-token
// assumption per cadence (or a rolling per-route avg-tokens read when one exists). Signature declared in P3.
public interface IRateCardQuery
{
    Task<decimal> EstimatePerLapUsd(string modelId, CoachCadence cadence, CancellationToken ct);
    Task<decimal> EstimatePerSessionUsd(string modelId, CancellationToken ct);
}

// Settings — interface + SQLite impl over the EXISTING settings table, in P3 (keys = §3.8 of the UI doc).
public interface ISettingsStore
{
    Task<string?>  GetModelIdAsync(string cadenceKey, CancellationToken ct);   // "corner"|"sector"|"lap"|"debrief"
    Task           SetModelIdAsync(string cadenceKey, string modelId, CancellationToken ct);
    Task<decimal?> GetMonthlyBudgetAsync(CancellationToken ct);
    Task           SetMonthlyBudgetAsync(decimal usd, CancellationToken ct);
    Task<bool>     GetVoiceEnabledAsync(CancellationToken ct);
    Task           SetVoiceEnabledAsync(bool enabled, CancellationToken ct);
    // … voice engine, locale, race-mode, theme/accent/canvasTone, hotkeys — string key/value
}
// SETTINGS→RUNTIME READ PATH (so the headline "Модель · корнер/дебриф" selector is not inert):
// P3 registers a SqliteSettingsConfigurationSource that surfaces the settings table (model.corner/
// model.sector/model.lap/model.debrief, budget.monthly_usd, reasoning.debrief) into the same
// configuration that binds IOptionsMonitor<LlmOptions>. A SetModelIdAsync write reloads the source, so
// LlmRouter's NEXT route resolution sees the new modelId (appsettings.json is the fallback when no
// override row exists), and the RuleEngine budget guard reads budget.monthly_usd the same way. The
// alternative (LlmRouter/RuleEngine consult ISettingsStore directly with appsettings fallback) is
// equivalent; the IConfigurationSource form is chosen so existing IOptions binding is untouched.

// Reference library + session history — signatures DECLARED in P3, impls land P6/P7.
public interface IReferenceQueryRepository
{
    Task<IReadOnlyList<ReferenceLap>> ListAsync(string? trackId, string? carId, string? weatherBucket, CancellationToken ct);
    Task SetPinnedAsync(string referenceId, bool pinned, CancellationToken ct);
}
public interface ISessionHistoryRepository
{
    Task<IReadOnlyList<SessionSummary>> ListAsync(SessionFilter? filter, CancellationToken ct);
    Task<IReadOnlyList<CoachTipRow>>    GetSessionTipsAsync(string sessionId, CancellationToken ct);
}
```

`CostByRoute`/`CostByDay`/etc. are records with `IReadOnly*` surfaces. Budget enforcement reuses
`RuleEngineOptions.SessionBudgetUsd` (per-session guard) + `budget.monthly_usd` checked against
`GetRolling30DayCostAsync`.

---

## Concrete schemas / sketches

### Gold artifact — `cadence:"corner"` (derived 1:1 from `CornerEvent`; only Gold-tier scalars leave the machine)

```jsonc
{
  "schema_version": "gold/1", "cadence": "corner", "locale": "ru-RU",
  "session": { "track_id": "spa", "car_class": "gt3", "weather": "dry",
               "lap_number": 7, "has_reference": true },
  "event": {
    "corner_id": "spa_t02", "corner_name": "Eau Rouge", "sector_idx": 0,
    "delta_ms": 140, "brake_point_diff_m": -3.4, "min_speed_diff_kmh": -5.1,
    "throttle_resume_diff_m": -2.8, "racing_line_deviation_m": 0.7,
    "trail_brake_pct_self": 0.22, "trail_brake_pct_ref": 0.41,
    "understeer_score": 0.71, "oversteer_score": 0.12,
    "wheelspin_score": 0.18, "brake_overlap_steer_pct": 0.31,
    "steering_jitter": 0.09,
    "off_track": false, "reason": "low_min_speed"
  },
  "valid_actions": [
    { "id": "wider_entry", "hint": "understeer + low min speed" },
    { "id": "brake_later_by_meters", "hint": "braked ~3 m early" }
  ],
  "phrase_limits": { "max_words": 8 }
}
```
The `wheelspin_score`/`brake_overlap_steer_pct`/`steering_jitter` fields are the **B1** kernel outputs
(D0) the registry's wheelspin/brake-overlap/steering-jitter actions reference — added, not dropped.
When `has_reference=false`, all `*_diff_*` / `delta_ms` / `*_ref` / deviation fields are **dropped**
(not sent as misleading zeros); the registry filter removes `requires_reference` actions, the
reference-free M6 actions can still fire, and the resulting tip is flagged `NoPbYet=true`. Floats
rounded (1 dp m/kmh, 2 dp scores) to cut tokens. The corner `reason` is the per-corner dominant-loss
reason that `ComputeService`/`CornerEventBuilder` already computes; D0 exposes it on `CornerEvent`
(append-only proto field) so both the corner Gold and B2's `dominant_reason` have a real source.
Sector/lap envelopes carry `top_losses[≤3]` + `top_corner` from `SectorEvent`/`LapEvent`, plus a
`tyre_temp`/`brake_temp` abuse summary at **lap** cadence (owner decision — tyre/brake-temp driving advice
is in P3). The session/debrief envelope carries `aggregated_losses` (B2 — `corner_id` only at the event
layer; `GoldArtifactBuilder` resolves the human `corner_name` via `CornerNameMap` at the Coach layer, per
ADR-0010), `understeer_trend`, `stints`, per-sector aggregate deltas, consistency stddev, theoretical-best
gap, PB/avg, **and a derived session-level fuel/tyre summary scalar** (avg fuel-per-lap + end-of-session
tyre-wear/deg — closes FR-060's "fuel/tyre summary" element; raw fields are plumbed in D0) (M3).
`phrase_limits.max_words` is per-cadence (8 corner / 25 sector / 25 lap / 200 session), driven by
`CoachOptions` — see D1.

### Action registry entry (embedded JSON; `when` is typed data, never an expression string)

```jsonc
{ "id": "brake_later_by_meters", "label_short": "brake_later", "cadence": "corner",
  "priority": { "phase": "brake", "rank": 120 }, "requires_reference": true,
  "when": [ { "field": "brake_point_diff_m", "op": "lt", "value": -2.0 },
            { "field": "off_track", "op": "eq", "value": false } ],
  "params": [ { "name": "corner", "from": "corner_name" },
              { "name": "meters", "from": "brake_point_diff_m", "transform": "abs_round0" } ],
  "phrase_template_ru": "В {corner} тормози позже на {meters} м." }
```
`label_short` is the authored chip label carried into `CoachTip.ActionLabelShort` (so the overlay never
trims `_by_meters` itself). `op ∈ {lt,lte,gt,gte,eq,neq,abs_gt,abs_lt}`; `field` validated at load against
a **checked-in Gold field-name set** (the loader's fail-fast in PR-C). This load-time check and B3 #4's
`ValidateOnStart` (PR-F) cross-check are the **same checked-in field-name source**: PR-C validates against
the static set; PR-F's `ValidateOnStart` (after PR-D ships the `GoldArtifact` records) re-validates that
the static set matches the *real* per-cadence Gold record shape, so the two layers cannot drift (the PR-F
check is the authoritative superset; the PR-C check is the early subset against the same names). This is
exactly why D0 must add the B1 fields. **Priority is a total order (M5):** a `(phase ∈ {brake,entry,apex,
exit}, rank int)` pair compiled to a single comparable key (`phase` weight dominates, `rank` breaks ties,
metric magnitude breaks remaining ties at build time), so a brake-phase root cause outranks an exit-phase
symptom and `Take(MaxActionsInMenu=5)` is deterministic. The overlay's coarse `Severity` chip is a
separate deterministic projection of that key via `CoachOptions.SeverityBands` (config thresholds), kept
out of the sort key. Valid subset = `cadence match ∧ (has_reference ∨ ¬requires_reference) ∧ all(when)`,
ordered by the total order, `Take(5)`. **Empty subset ⇒ no LLM call** (the common clean-corner case).

**Registry actions added per M6 (in the data, not prose):**
- `ease_understeer` (`understeer_score > 0.7`, `requires_reference=false`) and `settle_oversteer`
  (`oversteer_score > 0.6`, `requires_reference=false`) — the ≥2 reference-free corner actions that let
  the overlay render a useful tip while `NoPbYet=true`.
- `overdrove_entry` (`brake_point_diff_m > 2` AND `min_speed_diff_kmh < -3` AND `off_track == false`).
- `wheelspin_on_exit` (`wheelspin_score > τ`), `brake_overlap_steer` (`brake_overlap_steer_pct > τ`),
  `steering_jitter` (`steering_jitter > τ`) — the B1-backed quality actions.
- `tyre_overheat` / `brake_overheat` at **lap** cadence (`tyre_temp_c`/`brake_temp_c` over the abuse
  threshold) — owner: overheat-from-abuse driving advice is in P3.
- Gated per-cadence **catch-alls** with an explicit **delta-floor when-clause** (e.g.
  `abs(delta_ms) > FloorMs`) so a clean corner stays silent — the catch-all only fires when there is a
  real loss to talk about, preserving corner silence.

**Reserved (declared, never selected in MVP):** engine-map / ABS / TC **advice** actions and the
**Strategy** (pit) actions are *not* in the shipped registry; the data fields are plumbed (D0) and the
`Strategy` cadence + quiet-zone exist, but no such action is loaded — a later race-craft phase adds them.

### Output JSON-schema (real-time) — `action_id` enum is *exactly* the valid subset, regenerated per request

```jsonc
{ "type": "object", "additionalProperties": false,
  "required": ["action_id", "phrase_ru"],
  "properties": {
    "action_id": { "type": "string", "enum": ["wider_entry", "brake_later_by_meters"] },
    "phrase_ru": { "type": "string" } } }
```
**No length constraints on the wire** — neither `maxLength` nor `minLength`. Because the real-time path
routes through the gateway to Gemini, whose `responseSchema` rejects keyword constraints, the schema stays
constraint-free and **non-empty + word-limit are both enforced post-parse** in C# from the per-cadence
`CoachOptions` value (empty or over-limit ⇒ treated as a schema failure ⇒ retry-where-allowed/template).
The same `CompleteAsync` schema serves corner/sector/lap. The hard generation bound is the per-route
`MaxOutputTokens`, sized for Cyrillic density (~2.0–2.5 BPE tokens per Russian word): **corner 96 /
sector 192 / lap 192** output tokens. A small flat schema + small enum maximizes provider compliance.

### Output JSON-schema (debrief, session cadence — built but live-LLM disabled in P3)

```jsonc
{ "type": "object", "additionalProperties": false,
  "required": ["top_losses", "top_priority", "setup_hint"],
  "properties": {
    "top_losses": { "type": "array", "maxItems": 5, "items": {
      "type": "object", "additionalProperties": false, "required": ["corner","ms","why"],
      "properties": { "corner": {"type":"string"}, "ms": {"type":"integer"},
                      "why": {"type":"string"} } } },
    "top_priority": { "type": "string" },
    "setup_hint":   { "type": ["string","null"] } } }
```
`setup_hint` is in `required` (OpenAI/OpenRouter `strict:true` requires *every* property in `required`)
but stays semantically optional via the `["string","null"]` union. **The schema strategy is keyed on the
resolved model FAMILY, not the gateway provider (D5):** the real-time Gemini route gets the `nullable:true`
+ constraint-stripping rewrite (Gemini's `responseSchema` rejects `minimum`/`maxLength`/`minItems` and the
union-null shape), while the debrief Anthropic-Sonnet route — *also fronted by `OpenRouterProvider`* — does
**not** natively support OpenAI-style strict `json_schema` (Anthropic uses tool-emulation). **Before the
debrief live call is enabled, the plan verifies OpenRouter actually enforces strict `json_schema` (and the
`["string","null"]` `setup_hint` union) for `anthropic/claude-sonnet-4.6`; if it does not, the debrief
schema strategy switches to forced-tool and the degraded path is documented.** When strict is silently
ignored, the only safety net is post-parse validation → template, so this must be confirmed, not assumed.
Per **m2**, `top_losses` carries an explicit `maxItems` **and** a post-parse cap so `aggregated_losses`
boundedness is enforced, not implied. A schema-lint test asserts `required == keys(properties)` for every
strict schema; a per-model-family schema-acceptance fixture (real, not FakeProvider) guards portability —
and is the gate that catches OpenRouter+Sonnet strict non-compliance before the default is pinned live.

### System prompts (per cadence, M4) + RU few-shots (double as golden fixtures)

`coach.system.v1.ru.txt` (real-time):
```
Ты — русскоязычный гоночный инженер-наставник (ACC, GT3). Тебе дают JSON «Gold» с уже посчитанными
отклонениями пилота от его эталонного круга и список допустимых действий valid_actions.
Правила:
1. Выбери РОВНО один action_id ТОЛЬКО из valid_actions. Другие id запрещены.
2. Одна короткая подсказка по-русски: не больше {max_words} слов, повелительное наклонение,
   называй место по имени (corner_name ИЛИ top_corner — что есть в Gold).
3. Не выдумывай цифр, которых нет в Gold. Число можно назвать ТОЛЬКО если оно явно есть в Gold
   (метры тормоза, км/ч, секунды); иначе — без цифр.
4. Не упоминай телеметрию, JSON или «эталон» дословно. Тон спокойный, как по радио в гонке.
5. Ответь СТРОГО в JSON по схеме: {"action_id": "...", "phrase_ru": "..."}.
```
`coach.system.debrief.v1.ru.txt` (session cadence, M4): a longer prompt that consumes
`aggregated_losses` + per-sector aggregates + consistency + stints, asks for `top_losses`/`top_priority`/
`setup_hint`, allows numbers (they exist in Gold), and forbids inventing setup advice when
`setup_hint`'s inputs are absent. `PromptOptions` selects `{SystemVersion, FewShotVersion}` **per
cadence**. Few-shot set (each stored as an exact request/response pair, doubling as a mocked-handler
golden): a corner positive, a **sector** example, a **lap** example, a **no-PB** example (reference-free
action, no numbers), a **debrief** example, and a **negative example** (model picks an out-of-subset id /
invents a number → shows the rejected shape) to anchor the number rule.

```jsonc
// coach.fewshot.v1.ru.json — corner positive (also the mocked-handler golden)
{ "user": { "cadence": "corner", "event": { "corner_name": "Eau Rouge", "min_speed_diff_kmh": -6.2,
            "understeer_score": 0.74, "brake_point_diff_m": -3.1, "off_track": false },
            "valid_actions": [ { "id": "wider_entry", "hint": "understeer + low min speed" } ],
            "phrase_limits": { "max_words": 8 } },
  "assistant": { "action_id": "wider_entry", "phrase_ru": "Шире вход в Eau Rouge, неси скорость." } }
```

---

### D0. Compute kernel extensions + SessionLossAccumulator + strategy data plumb (`Pipeline`, `Reference`, `Contracts`) — B1, B2, M7, owner pit/engine plumb

The compute-layer prerequisite that makes the registry loader pass (B3 #4) and the debrief real (B2).

- **B1 — add the tip-quality kernels** (pure Pipeline functions, mirroring C4's style, named-constant
  thresholds, flagged heuristic where a score has no native channel):
  - **wheelspin** ← `wheel_slip` / `SlipRatio` on exit → `wheelspin_score`.
  - **brake-overlap-steer** ← fraction of the corner with `brake_pct>τ` ∧ `|steer_rad|>τ` →
    `brake_overlap_steer_pct` (distinct from trail-brake: this flags *unwanted* overlap).
  - **steering-jitter** ← variance of steer-rate over the corner window → `steering_jitter`.
  - **tyre/brake-temp abuse** ← `tyre_temp_c`/`brake_temp_c` (proto) vs an abuse band → a lap-cadence
    overheat summary (owner: overheat-from-abuse driving advice is in P3, **lap** cadence).
  **These are append-only protobuf edits, not C# record edits:** new fields are appended (new field
  numbers, never reorder) to the `CornerEvent` message (`wheelspin_score`, `brake_overlap_steer_pct`,
  `steering_jitter`, **and `reason`** — the per-corner dominant-loss reason `CornerEventBuilder` already
  computes internally, now surfaced so the corner Gold and B2 have a source) and the lap-temp summary to
  `LapEvent`, in `src/SimCoach.Contracts/Schemas/telemetry.proto` (protoc regen). The kernels live in
  `SimCoach.Pipeline/Kernels`; the values are written into the events in `SimCoach.Reference`
  (`CornerEventBuilder`/`ComputeSession`). The Phase-2 golden event stream is regenerated to include them
  (Runtime-touching the live `ComputeService`).
- **B2 — `SessionLossAccumulator`** (`internal sealed`, mutation isolated): subscribes to nothing
  itself — `ComputeService` feeds it each `CornerEvent`/`LapEvent`; it accumulates per-`corner_id`
  totals and on `SessionEvent` emits a **new repeated `AggregatedLoss` message** (appended to the proto)
  `aggregated_losses[]{corner_id, total_loss_ms, avg_loss_ms, sample_count, dominant_reason}`, **bounded**
  (top-N by `total_loss_ms`, the same cap the schema's `maxItems` enforces post-parse, m2). `dominant_reason`
  is the modal `CornerEvent.reason` for that corner (sourced from the field D0 adds above). **No
  `corner_name` lives on the event** — names stay out of compute per ADR-0010; the human `corner_name` is
  resolved at the Coach layer by `GoldArtifactBuilder` via `CornerNameMap` (PR-C) when it builds the
  debrief Gold, so PR-B has no dependency on PR-C. This is the only source for the debrief `РАЗБОР` prose.
- **M3 envelope on `SessionEvent`:** populate `stints` (still `[]` in MVP — proto-valid, race-craft
  later), per-sector aggregate deltas, lap-time consistency (stddev), theoretical-best gap, and a
  **derived session fuel/tyre summary scalar** (avg fuel-per-lap + end-of-session tyre wear/deg, Gold-tier
  — closes FR-060's fuel/tyre-summary element), so the debrief Gold/schema (D3) has real inputs; `setup_hint`
  is fed from `understeer_trend`/tyre-degradation, or omitted (never a dangling key). All append-only proto.
- **M7 — no compute change:** `normalized_car_position` is **already a frame field** (`telemetry.proto`),
  so D0 adds nothing here. The gate-only frame snapshot struct + corner-phase marker that consume it are a
  **Coach-layer type that lands with the RuleEngine in PR-G** (D7) — attributed there, not to PR-B, so a
  reviewer is not led to believe the gate source is delivered before its consumer exists.
- **Owner strategy/pit plumb (P3-reserve, data only):** append-only proto fields + `AccFrameMapper`
  lines for the strategy inputs not yet mapped — ACC graphics `EngineMap`, `Tc`, `TcCut`, `Abs`, plus
  pit-state; the fuel/`fuel_per_lap_l`/`tyre_wear_pct`/`tc_active`/`abs_active`/temps already exist in
  the proto. These flow frame→Gold-input only; **no Strategy advice action consumes them in MVP.**
- **Tests:** each kernel on hand-built corner traces with known answers; degenerate laps return
  sentinel/zero without throwing; `SessionLossAccumulator` over a synthetic event stream →
  expected `aggregated_losses` (order, totals, `dominant_reason`, top-N cap); M3 envelope fields
  populated; mapper golden for the new strategy fields (append-only, old fixtures replay `0`); the
  Phase-2 e2e golden regenerated and still green.

### D1. ActionRegistry + RU phrase templates (`SimCoach.Coach`, data) — FR-032, M5, M6

- Embedded `Data/actionRegistry.json` (`schema_version:"actions/1"`), loaded once into an immutable
  `IReadOnlyList<CoachAction>`. ~24 MVP actions across corner/sector/lap cadences (`brake_later_by_meters`,
  `wider_entry`, `higher_min_speed`, `more_trail_brake`, `earlier_throttle`, `tighten_apex`,
  `lift_partial_throttle`, the M6 reference-free `ease_understeer`/`settle_oversteer`, `overdrove_entry`,
  the B1-backed `wheelspin_on_exit`/`brake_overlap_steer`/`steering_jitter`, the lap-cadence
  `tyre_overheat`/`brake_overheat`, …) + a **gated catch-all per cadence** with an explicit delta-floor
  when-clause so the subset is rarely empty when something is actionable yet a clean corner stays silent.
- `WhenClause`/`ClauseOp`/`ParamBinding`/`ParamTransform` records + a `label_short` per entry; a small
  `Evaluate(clause, event)` switch (pure, no `dynamic`, no reflection). Loader validates every `field`
  against the **checked-in Gold field-name set** and **fails fast on an unknown field** (this is why D0's
  B1 fields must exist first; PR-F's `ValidateOnStart` later re-checks that set against the real `GoldArtifact`
  records — same source, no drift). Template renderer fills `phrase_template_ru` from `params` and yields
  both the **`RenderedParam`** chip value and the entry's **`ActionLabelShort`** the `CoachTip` carries.
- **Total-order `Priority` (M5):** `(phase, rank)` compiled to a single comparable key; the valid-subset
  filter orders by it (no `{high,medium,low}` ties). A golden test pins a fixed ordering on a fixture
  Gold so `Take(5)` is deterministic and a brake-phase cause outranks an exit-phase symptom. The discrete
  **`CoachSeverity` band** the overlay chip renders is derived deterministically from that key via config
  `CoachOptions.SeverityBands` (Rank/phase thresholds) — a separate projection, never the sort key, so the
  "high" chip has a stable source without re-introducing the deleted enum into ordering.
- Length budgets are config (`CoachOptions`: `InCornerMaxWords=8`, `SectorMaxWords=25`, `LapMaxWords=25`,
  `DebriefMaxWords=200`, matching `FR-033`) — no magic numbers. `CoachOptions.RouteKeys` maps
  `CoachCadence → RouteKey` (default `corner`/`sector`/`lap`/`debrief`/`strategy`); `CoachOptions.SeverityBands`
  maps the priority key → `CoachSeverity`.
- **Tests:** registry loads + schema-validates; unknown-field reference fails load; each `when` predicate
  evaluates per `ClauseOp`; total-order priority sorts deterministically; **`SeverityBands` projects a
  fixed Rank → expected `CoachSeverity`**; template substitution + transforms (`abs_round0`) produce
  expected RU + `RenderedParam` + `ActionLabelShort`; valid-subset filter on golden Gold fixtures
  (cadence/requires-reference/priority ordering/`Take`); reference-free M6 actions survive
  `has_reference=false`; the gated catch-all stays silent on a clean corner.

### D2a. Corner-name injection — short + spoken forms (`SimCoach.Coach`) — FR-031 (ADR-0010/0014), m4

- `CornerNameMap` (exists) resolves `corner_id → name`; positional fallback `поворот {N}` parsed from the
  trailing `_tNN`, sourced from `Resources/CoachStrings.ru.resx` (RU is user-facing → `.resx`). Unknown
  track ⇒ all positional. Names are **first-party, baked** per ADR-0014.
- **m4 — two derived display forms** added at this layer (consumed by `CoachService` at emit, fed into
  `CoachTip`): `CornerNameShort` (e.g. `О-Руж` for Eau Rouge — abbreviated display form for the slim
  overlay card) and `CornerNameSpokenRu` (strip a trailing `(N)`, expand to an RU ordinal — the P4 voice
  path). Both are user-facing → `.resx`.
- **Tests:** corner-name injection (hit + positional fallback + unknown track); short form + spoken form
  (ordinal expansion, `(N)` strip).

### D2b. PromptBuilder (`SimCoach.Coach`) — FR-031, FR-033, M4

- `PromptBuilder` loads versioned system/few-shot resources via `IOptions<PromptOptions>` **per cadence**
  (`SystemVersion`/`FewShotVersion`/`OverridePath` selected by `CoachCadence`, incl. the dedicated
  `coach.system.debrief.v1.ru.txt` + debrief few-shot); provider-neutral, emits a `LlmRequest` (RouteKey
  from `CoachOptions`). Generalizes the real-time naming rule from `corner_name` to
  `(corner_name | top_corner)` (M4).
- **Tests:** prompt assembles the selected version per cadence; debrief prompt selected for session
  cadence; override path read; few-shot round-trips as a request/response pair.

### D3. GoldArtifactBuilder per cadence (`SimCoach.Coach`) — FR-030, FR-060, M3

- One `Build*` method per cadence consuming the matching `DomainEvent` payload (+ session context). Emits
  the deterministic JSON above: corner (1:1 from `CornerEvent`, incl. the B1 `wheelspin_score`/
  `brake_overlap_steer_pct`/`steering_jitter`), sector (`top_losses`+time), lap (time/delta/is_pb/
  is_clean/`top_losses` + tyre/brake-temp abuse summary), session/debrief (counts, PB/avg,
  `understeer_trend`, **`aggregated_losses`** [B2] — the builder **resolves each `corner_id` to its human
  `corner_name` via `CornerNameMap` here, at the Coach layer** (names stay out of compute per ADR-0010), so
  the debrief Gold carries the named losses the LLM/template need — per-sector aggregate deltas, consistency
  stddev, theoretical-best gap, the derived **fuel/tyre summary** (FR-060), `stints` [`[]` in MVP],
  `setup_hint` fed-or-omitted) [M3]. Determinism: same input → same JSON, no timestamps/UUIDs; floats rounded.
- **Derived corner scalar obligation (`trail_brake_diff_pct`) — surfaced by the PR-C review:** the corner
  builder **must emit a derived `trail_brake_diff_pct = trail_brake_pct_self − trail_brake_pct_ref`** scalar.
  PR-C's `actionRegistry.json` already references it (the `more_trail_brake`/`less_trail_brake` actions: the
  registry `WhenClause` is field-vs-constant, so the field-vs-field comparison the stale doc implied is encoded
  as a single derived field instead) and PR-C's `GoldFieldNames` corner set lists it — so if D3 omits it those
  two actions never fire. It is a Coach-layer derivation (like `corner_name`), **not** a proto/compute field,
  and is **dropped when `has_reference=false`** (it is reference-relative). PR-F's `ValidateOnStart` #4
  re-checks the registry fields against the real corner `GoldArtifact` record, so a missing derivation **fails
  host startup**, not silently at runtime. (`sector_idx` is **not** such an obligation — no PR-C action uses
  it; PR-C dropped it from the corner field-name set.)
- **Bool-field population obligation (`off_track`, `is_pb`, `is_clean`, `tyre_overheat`, `brake_overheat`) —
  surfaced by the PR-C review:** PR-C's `ClauseEvaluator` is **fail-closed** — an `eq`/`neq` clause on a field
  the Gold view does not carry evaluates to `false`, so the action silently never fires. `brake_later_by_meters`,
  `overdrove_entry`, and `higher_min_speed` all gate on `{off_track eq false}`; the lap actions gate on the
  thermal/`is_pb`/`is_clean` bools. The D3 Gold adapter (and PR-G's `IGoldView` wrapper over the typed records)
  **must always populate these bool fields**, not leave them absent. Unlike a missing *registry* field (caught
  by PR-F `ValidateOnStart` #4), a missing *runtime value* is not load-validated — so this is a correctness
  obligation on the Gold builder/adapter, not a startup check.
- **Privacy choke point:** `GoldArtifactBuilder`/`PromptBuilder` is the *only* place a Gold artifact is
  serialized to a string for `ILlmClient`. A serializer unit test asserts the JSON contains **no** forbidden
  raw fields (world coords, frame arrays, exact car id, raw strategy/fuel telemetry) — mechanically
  enforcing "only Gold-tier leaves the machine."
- **`has_reference=false` field-drop is D3's responsibility (surfaced by the PR-B/D0 review, #8):** the
  reference-relative **session/M3** fields PR-B emits as proto-default `0` without a PB —
  `sector_avg_delta_ms` and `theoretical_best_gap_ms` (alongside the corner `*_diff_*`/`delta_ms`/`*_ref`
  fields already noted) — must be **dropped from the Gold artifact**, not serialized as misleading zeros that
  read as "perfectly on reference." PR-B intentionally leaves them at `0` (matching the existing
  `CornerEventBuilder` honest-zeros contract); the drop happens here at the Coach/Gold layer.
- **Tests:** synthetic events with known deltas → expected fields/values (incl. B1 fields and
  `aggregated_losses`); `has_reference=false` drops the diff fields **and the M3 `sector_avg_delta_ms`/
  `theoretical_best_gap_ms`**; first-session / no-PB / off-track edge cases; the `aggregated_losses` post-parse
  cap (m2); the privacy-serializer assertion.

### D4. PromptBuilder integration + output-schema compilation (`SimCoach.Coach`) — FR-031, FR-033, M2/M4

- Composes the output JSON schema per request with `action_id.enum` = the valid subset (the single biggest
  reliability lever) and `SchemaName`. Injects `valid_actions` (id + English `hint`, not user-facing) into the
  user message. RU word limits enforced post-parse (D8), not in schema; `max_words` is per-cadence. The
  debrief schema's `top_losses.maxItems` + post-parse cap bound `aggregated_losses` (m2).
- **Tests:** schema validates a correct LLM output; rejects out-of-subset `action_id`; rejects overlong
  phrase post-parse; empty subset never reaches schema build; schema-lint (`required == keys(properties)`);
  debrief schema bounds `top_losses` to `maxItems`.

### D5. ILlmClient seam: LlmRouter + OpenRouterProvider + FakeProvider (`SimCoach.LLM`) — FR-031, FR-061, M1/M2

- Land the revised Ring-0 contract (PR-A) then Ring-1 `LlmRouter` + Ring-2 adapters. `ILlmClient` is a
  decorator chain: `LlmRouter` resolves `RouteKey → (providerId, modelId, MaxOutputTokens, Timeout,
  Reasoning, Stream)` from `LlmOptions.Routes`, then delegates through the **per-provider CircuitBreaker
  decorator** (D7) and the **CostMeter decorator** (D6) to the chosen `ILlmProvider` — each a separate type.
  `OpenRouterProvider` (typed `HttpClient`, `AuthHandler`, OpenAI-family `ISseDecoder`, usage→`LlmUsage`,
  HTTP→`LlmFailure`) selects its `ISchemaTranslator` **by the resolved `modelId`/family, not by being
  "OpenRouter"** — because the one adapter fronts both Gemini and Sonnet. `FakeProvider` (keyed default,
  deterministic fixture echo, no network) keeps CI/replay green.
- `ISchemaTranslator` is keyed on the resolved model FAMILY: OpenAI/Gemini-via-OpenRouter strict route →
  `response_format:{json_schema,strict:true}` with the Gemini constraint-strip + `["string","null"]`→
  `nullable:true` rewrite; **Anthropic family → forced-tool emulation** (Anthropic does not honour
  OpenAI-style strict `json_schema` natively), used for the debrief unless the live check confirms OpenRouter
  enforces strict for `claude-sonnet-4.6`; `json_object`-only providers → inject the schema into the system
  prompt. A **per-model-family schema-acceptance fixture** test (real HTTP shape, not just FakeProvider)
  asserts each family's translated schema is accepted, and is the pre-pin gate for the debrief default.
- Keyed DI per `providerId` string — **two ids for the one shipped `OpenRouterProvider`**:
  `openrouter-google` (real-time) and `openrouter-anthropic` (debrief), so the per-`providerId` breaker is
  isolated per upstream (D7). `AddLlm(cfg)` binds `LlmOptions` with `ValidateOnStart` (the B3 checklist
  below). **Per-route config (M1/M2):** real-time `Stream=false`, `Reasoning=Off`, **timeouts corner 2000
  / sector 2500 / lap 3000 ms**, `MaxOutputTokens` 96/192/192; **debrief route** → `anthropic/claude-sonnet-4.6`
  (`claude-sonnet-4-6`, $3/$15), **`Reasoning=Low`**, `Timeout=8000 ms`. **`MaxOutputTokens=2000` for the
  debrief, NOT 640:** Reasoning=Low (adaptive thinking) emits thinking tokens that bill as output **and
  consume the same `max_tokens` budget**, and a 200-word RU JSON debrief is already ~480–590 visible tokens;
  640 would leave no headroom for thinking → `finish_reason=max_tokens` truncation → invalid JSON →
  template on most debriefs. 2000 covers Low-effort thinking + the structured 200-word output. The
  ~1.8¢/session estimate is reconciled to assume bounded Low-effort thinking (a few hundred output-billed
  tokens), not negligible thinking. `anthropic/claude-haiku-4.5` documented middle ground; the DeepSeek
  provider is **registered but config-gated OFF** (M2, vLLM #41132). `StreamAsync` declared, throws
  `NotSupported` until P6. The **reserved `strategy` route is bound to a real rated provider** in
  `appsettings.json` (it is never *called* in MVP, but must satisfy ValidateOnStart #1/#2 — the validators
  do not special-case unused routes).
- **Tests (no live network):** mocked `HttpMessageHandler` golden fixtures → `OpenRouterProvider` parses a
  Success (token/usage map) and each `LlmFailure` variant (429+Retry-After, 5xx, transport, schema, auth);
  timeout → `Failure.Timeout`; router resolves `RouteKey→provider` and routes to `FakeProvider` by default;
  per-model-family schema-acceptance fixtures; **debrief route does not truncate** (a representative Low-effort
  response returns `finish_reason ≠ max_tokens` under `MaxOutputTokens=2000`); **a settings `model.corner`
  override changes the `modelId` the router resolves** (the SQLite config source re-bind).

### D6. CostMeter → SQLite `llm_usage` + cost-query API (`SimCoach.LLM` + `Storage`) — FR-036, FR-072

- `ICostMeter.RecordAsync(LlmCallInfo, LlmUsage)` computes
  `cost = (In−Cached)/1e6·InPerM + Cached/1e6·CachedInPerM + (Out+Reasoning)/1e6·OutPerM`
  (reasoning tokens bill as output — included now so the `SessionBudgetUsd` guard stays correct the day a
  thinking model like Sonnet-4.6-debrief is configured), rates read from
  `LlmOptions.Providers[providerId].Rates[modelId]` — **never hard-coded**.
- Persists via the **extended `llm_usage` table — migration `002_llm_usage_cost.sql` adds `provider` and
  `cached_input_tokens` ONLY** (`model_id` already exists in `001`; re-adding it would throw
  `duplicate column name` and crash host startup). PR-F also adds a **contiguity assertion** to
  `DatabaseMigrator` (fail-fast on a gapped/out-of-order version) so a future numbering mistake cannot
  silently shadow a migration.
- **`ICostQueryRepository` (UI contract, implemented now):** `GetSessionCostAsync`,
  `GetRolling30DayCostAsync`, `GetCostByDayAsync(days)`, `GetCostByRouteAsync(fromUtc)` — feeding Screen
  06 (cost meter) and Screen 02's status bar (HISTORICAL spend). `RollupAsync` gives per-session +
  rolling-30-day spend (`FR-072`, ADR-0004). Writes are async, off the hot path.
- **`IRateCardQuery` (UI contract, signatures declared now):** Screen 04 renders a **forward** estimate
  next to a model the user is about to switch to (`~$0.002 / круг`, `~$0.01 / сессия`) — historical spend
  cannot price a never-used model, so this prices from the config rate-card × a typical-token assumption per
  cadence (`EstimatePerLapUsd`/`EstimatePerSessionUsd`). The rate-card it reads is the same `Rates` table
  ValidateOnStart #1 already guarantees is complete.
- **Tests:** cost math per model incl. cached **and reasoning** tokens; rollup sums; rate pulled from
  config not constant; rolling-window correctness with an injected clock; `GetCostByRouteAsync` groups by
  route+provider+model; migration `002` on a `001` DB adds exactly the two missing columns (no duplicate).

### D7. CircuitBreaker per provider + RuleEngine quiet zones (`SimCoach.LLM`, `SimCoach.Coach`) — FR-037, FR-035, M7

- `ICircuitBreakerRegistry.For(providerId)` → one in-memory breaker **per provider id** (a Gemini outage
  must not open Anthropic). **Isolation is real only because the single `OpenRouterProvider` is registered
  under two distinct ids** — `openrouter-google` (real-time) and `openrouter-anthropic` (debrief) — so the
  real-time route's breaker tripping cannot open the debrief route's breaker. A *test asserts* a Gemini-route
  failure storm opening `openrouter-google` leaves `openrouter-anthropic` Closed. Defaults per `FR-037`:
  Closed → (`FailureThreshold=3` trip-worthy failures in a
  `Window=60 s`) → Open for `BreakDuration=60 s` (or `Retry-After` if longer) → Half-Open (single probe) →
  Closed/Open — all config (`CircuitBreakerOptions`). **Only infra failures trip it**
  (`RateLimited`/`ServerError`/`Transport`/`Timeout`); `SchemaViolation` is model-quality, handled by the
  Coach retry/template path, not the breaker. When open and the route has a `FallbackRouteKey`, the router
  downgrades; else `Failure.CircuitOpen` → template. `TimeProvider`-injected (fake clock, **no `Thread.Sleep`**).
- `RuleEngine.ShouldSpeak(gold, subset, frame, clock)` (pure, `IOptions<RuleEngineOptions>`, `TimeProvider`,
  latest-frame snapshot incl. **`normalized_car_position` + corner-phase marker, M7**). Quiet zones (all
  config-driven, covering `FR-035`): empty subset · per-cadence cooldown (corner 4 s / sector 8 s / lap
  none) · high driver workload (brake+steer / steer-rate) · on-a-straight suppression · **apex-window**
  suppression (from `normalized_car_position` + corner-phase) · **recent-contact** and **recent-off-track**
  debounce · **user-set quiet zones** (config track-position ranges, now non-stub because M7 gives them a
  position source) · session-not-green (pit/SC/yellow/replay-paused) · **strategy quiet-zone (reserved)**
  — a declared `Strategy`-cadence gate that, in MVP, simply suppresses everything (no Strategy tip emitted)
  while reserving the seam for the deferred pit advisor's straight/pit-window timing · budget guard
  (CostMeter over `SessionBudgetUsd` → template-only) · low-priority floor. Lap/session cadence bypass
  straight/workload/apex gates but honor cooldown/budget/session-state/quiet-zones.
- **Tests:** breaker state machine (closed→open→half-open→closed) with fake clock; success resets; schema
  violation does **not** trip; fallback route on open. RuleEngine: each quiet zone (incl. apex/contact/
  off-track/user-zone via the M7 position field, and the reserved strategy-zone suppressing everything)
  gates correctly; cooldown via injected clock; empty-subset short-circuit.

### D8. CoachService + tip emission + ICoachTipSink (`SimCoach.Coach`) — FR-034, FR-014

- `BackgroundService` subscribing to `DomainEventFanOut` **in its constructor** (and the gate-only
  `TelemetryFanOut` latest-frame snapshot). Per-cadence orchestration: build Gold (D3) → valid subset (D4) →
  `RuleEngine.ShouldSpeak` (D7) → `ILlmClient.CompleteAsync` (D5) → **cadence-aware validation**:
  - **real-time (corner/sector/lap):** `{action_id ∈ subset, wordCount(phrase_ru) ≤ per-cadence max, non-empty}`.
  - **debrief (session):** the schema has neither `action_id` nor `phrase_ru`, so the subset check is
    **skipped**; instead `{top_losses ≤ maxItems (post-parse cap, m2), top_priority non-empty, aggregate
    wordCount(top_losses[].why ⧺ top_priority ⧺ setup_hint) ≤ DebriefMaxWords(200)}`.
  → **cadence-aware retry**: on schema-violation **retry once** for sector/lap/debrief (stricter RU reminder
  appended) but **skip the retry for corner** (a second round-trip lands after the next corner — straight to
  template); **never** retry on timeout → **template fallback** on any unrecoverable failure. The fallback is
  cadence-specific: real-time renders the **highest total-order action**; **debrief renders a deterministic
  `DebriefTemplate` from `aggregated_losses` + per-sector aggregates** (the B2/M3 data already in Gold), so an
  LLM failure on the session cadence still yields a real `top_losses`/`top_priority` artifact, never an empty
  one. Builds the full **`CoachTip`** — `ActionLabelShort` + `RenderedParam` (from the registry entry /
  template renderer), total-order `Priority` **and the derived `Severity` band** (from `CoachOptions.SeverityBands`),
  `CornerName`/`CornerNameShort`/`CornerNameSpokenRu` (D2a), `ProviderModelId` (from `LlmCallInfo`), `NoPbYet`,
  `GeneratedAtUtc`. Records `TipSource`; `NoteTip` updates cooldown. **Drains the domain subscription to
  completion on shutdown** so the final `SessionEvent`-derived tip survives stop.
- **No-reference behavior (`FR-014`):** when Gold `has_reference=false`, only non-delta actions survive the
  subset (the M6 reference-free `ease_understeer`/`settle_oversteer` keep a tip possible) and the emitted tip
  is flagged `NoPbYet=true`. The full FR-014 *best-of-session-so-far* provisional reference is **deferred**
  (Reference-layer feature — see risk register); P3 ships the label + no-delta path.
- `ICoachTipSink` + `ConsoleTipSink` (structured Serilog line + insert a `coach_tips` row via
  `CoachTipRepository`, incl. `rendered_param`/`priority`). **`Strategy` cadence is never produced** — the
  seam exists, the orchestration short-circuits it. Session-cadence debrief tip wired through the same path
  but its **live LLM call is behind the disabled `Llm:Live` flag** (template/`FakeProvider` otherwise;
  streaming delivery is P6).
- **Tests:** mocked `ILlmClient` + a golden event stream → expected tip sequence + `action_id`/`ActionLabelShort`/
  `RenderedParam`/`Priority`/derived `Severity`/full+short+spoken name; schema-reject → retry → template
  (sector/lap), corner-reject → immediate template (no retry); timeout → immediate template; **debrief
  validation skips the action_id check and bounds the 200-word aggregate**; **debrief LLM failure → the
  deterministic `DebriefTemplate` (non-empty `top_losses`)**; `no_pb_yet` set when reference absent; clean
  cancellation **drains** the final `SessionEvent` tip (asserted).

### D9. Wiring + persistence/query/settings + end-to-end + offline testability (`App`, `Storage`)

- `CoachComposition.AddCoaching(builder)` + `AddLlm(cfg)`: register `ActionRegistry`, `CornerNameMap`,
  `GoldArtifactBuilder`, `PromptBuilder`, `RuleEngine`, keyed `ILlmProvider`s + `LlmRouter` as `ILlmClient`
  (CircuitBreaker + CostMeter decorators), `ICircuitBreakerRegistry`, `ICostMeter`, `ICostQueryRepository`,
  `IRateCardQuery`, `ISettingsStore` + its `SqliteSettingsConfigurationSource` (so settings model/budget
  overrides re-bind `IOptionsMonitor<LlmOptions>`/the budget guard), `CoachTipRepository`,
  `ICoachTipSink`→`ConsoleTipSink`, `CoachService` as a hosted service **between ComputeService and
  McapRecorderService** (stop order above). `CoachService` takes both the `DomainEventFanOut` and
  `TelemetryFanOut` subscriptions in its ctor.
- **Persistence:** migration `003_coach_tips.sql` (with `rendered_param` + `priority` columns); the
  `IReferenceQueryRepository`/`ISessionHistoryRepository` signatures are **declared** (interfaces +
  reserved `debrief` nullable columns: prose, checklist+checked, per-sector aggregate deltas, balance
  verdict, audio-artifact ref, `setup_hint`, **and a structured `top_losses_json` column** — explicit P3
  decision: the per-corner loss attribution that powers the debrief headline is persisted as a structured
  JSON array, not prose-only, so a future P6 session-history loss panel reads it without migrating against
  live data) so P6/P7 don't migrate against live data; their impls land later. `ISettingsStore` is
  implemented over the existing `settings` table with the provider-neutral keys from the UI doc §3.8
  (`model.corner`/`model.sector`/`model.lap`/`model.debrief`, `budget.monthly_usd`, `reasoning.debrief`,
  `voice.enabled`/`voice.engine`, `general.theme`/`ui.accent`/`ui.canvas_tone`, `general.language`,
  `ui.race_mode`, hotkeys, `privacy.gold_only_egress`, `Llm:Live`); the model/budget keys are surfaced into
  configuration via `SqliteSettingsConfigurationSource` so the headline selector is **not inert** (a
  `SetModelIdAsync` write changes the model the router next resolves; `budget.monthly_usd` feeds the
  RuleEngine guard). API key/provider → `secrets.json` (DPAPI on Windows), **not** `settings`, **not**
  Gold-egress.
- `appsettings.json`: `Coach` thresholds + `RouteKeys` (incl. reserved `strategy`), `Llm.Routes`/`Providers`/
  `Rates`/`CircuitBreaker`, per-route stream/reasoning/timeout/max-tokens (debrief Sonnet 4.6 / Reasoning=Low),
  `Prompt` versions per cadence, `Llm:Live=false` default, `budget.monthly_usd` default. Env override via
  `SIMCOACH_` prefix.
- **E2E (offline):** replay fixture → domain events (incl. B1 fields + `aggregated_losses`) → `CoachService`
  against `FakeProvider` golden fixtures → assert tips emitted with correct `action_id`/`RenderedParam`/
  `Priority`, `coach_tips` (incl. `rendered_param`/`priority`) + `llm_usage` rows written, `ICostQueryRepository`
  returns the session cost, the final-`SessionEvent` tip survives shutdown, and finalized counts unchanged
  (stop order honored). Observability: logs show subset (`when` pass/fail), scrubbed LLM request/response,
  breaker transitions, cost per call.
- Tick the Phase 3 checklist in `implementation-plan.md`; add KB notes (provider-seam map, retry/template
  policy, RuleEngine thresholds, the B1 kernel formulas, the `aggregated_losses` accumulation, the RU eval gate).

Definition of done: Phase 3 checklist in `implementation-plan.md` fully ticked; CI green (windows + macos,
no network); replay → tips + `coach_tips` (with `rendered_param`/`priority`) + `llm_usage` + a queryable
session cost verified end-to-end on macOS against `FakeProvider`; the live OpenRouter path compiles and is
unit-tested via mocked `HttpMessageHandler` but stays flag-off.

---

## `ValidateOnStart` checklist (B3 — one test each, host crashes at startup not at runtime)

`AddLlm`/`AddCoaching` register option validators that run at composition. Each item has a dedicated test:

1. **CostMeter rate coverage** — every route's `(providerId, modelId)` has input + output (+ cached) rates
   in `LlmOptions.Providers[…].Rates[…]`.
2. **Route/cadence completeness** — every `CoachCadence` (incl. the reserved `Strategy` mapping) resolves
   to a configured `RouteKey`, and every `RouteKey` resolves to a registered provider. The validators do
   **not** special-case never-called routes, so `appsettings.json` **must** bind the reserved `strategy`
   route to a real rated provider (it satisfies #1/#2 even though no Strategy tip is emitted in MVP).
3. **`FallbackRouteKey` acyclicity** — the route fallback graph has no cycle.
4. **Registry-field-vs-Gold validity** — every action-registry `when`/`param` field exists in the Gold
   artifact for that action's cadence (this is the check that B1's D0 kernels exist to satisfy).
5. **Positive timeouts / max-tokens** — all `MaxOutputTokens > 0` and all timeouts `≥ 100 ms`.
6. **Prompt-resource existence** — every referenced per-cadence system/few-shot resource (incl.
   `coach.system.debrief.v1.ru.txt`) resolves as an embedded resource or override path.

---

## Mergeable chunking (PR plan)

**Status:** ✅ **PR-A done** (`feat/phase-3-pr1`) — Ring-0 `ILlmClient` seam + records, `LlmOptions`/
`RouteOptions`/`ProviderOptions`/`ModelRate`, internal `LlmRouter`/`ILlmProvider`/`FakeProvider`/
`ResolvedRoute`, `CoachCadence`; 49 LLM + 1 Coach-cadence unit tests; build/format/full-suite green.
Implementation notes: `LlmResult.Failure`'s payload is named `Error` (CS0542 forbids a member matching the
enclosing record name — the §"Key C# contract" sketch above is updated to match); options ship as
`sealed record` (for `with`-ergonomics, within the records-over-classes rule).

✅ **PR-B done** (`feat/phase-3-pr2` → PR #17, merged to `main`) — D0: tip-quality kernels
(wheelspin/brake-overlap-steer/steering-jitter/thermal), `SessionLossAccumulator`, M3 envelope on
`SessionEvent`, append-only proto fields (`CornerEvent.wheelspin_score`/`brake_overlap_steer_pct`/
`steering_jitter`/`reason`, `LapEvent.ThermalSummary`, `SessionEvent.aggregated_losses`/`AggregatedLoss`),
ACC strategy plumb; Phase-2 golden regenerated.

✅ **PR-C done** (`feat/phase-3-pr3`) — D1 + D2a: embedded `actionRegistry.json` (**25** actions) + loader +
`WhenClause`/`ClauseEvaluator` + `PhraseRenderer`; lexicographic `CoachPriority` + `CoachSeverity`/`SeverityBand`
projection; `CoachOptions`; `GoldFieldNames` fail-fast catalog; `IGoldView`/`DictionaryGoldView` seam;
`CornerNameMap` positional/short/spoken forms + first repo `.resx` (neutral `CoachStrings.resx`). 74 Coach unit
tests; build/format/full-suite green. **Implementation notes / intentional deviations:**
(1) **`ActionRegistry.ValidSubset(IGoldView, CoachOptions)` returns `IReadOnlyList<CoachAction>`**, not
`IReadOnlyList<RenderedAction>` as the §"Module map"/pipeline sketch implied — filtering/ordering is kept
separate from rendering (`PhraseRenderer.Render`), so each PR-C commit builds standalone; **PR-G's
`CoachService` renders the subset** (it already orchestrates the renderer). (2) PR-C adds the derived
`trail_brake_diff_pct` corner field to the registry + catalog; **D3 owes its emission** (see the D3
"Derived corner scalar obligation" bullet). (3) The first `.resx` is named **neutrally** (`CoachStrings.resx`,
not `.ru.resx`) because `Directory.Build.props` sets `NeutralLanguage=ru-RU` — a culture-qualified name would
build a satellite assembly and return `null` under the default culture; the accessor is hand-rolled (no
designer codegen) with an explicit `CultureInfo` to satisfy CA1304/CA1305. PR-D…PR-H: todo.

Phase 3 ships as **8 PRs** (merge order = build order) that each merge to `main` without breaking it.
A PR is mergeable when CI stays green (build + `dotnet format --verify` + xUnit on windows+macos, **no
live network**) and the Phase-2 spine + host run unregressed. The live OpenRouter call is gated by
`Llm:Live=false`; the registered `ILlmClient` default routes to `FakeProvider`, so the host is always
runnable without an API key.

Safety classes (as in Phase 2): **Additive** · **Dead-until-wired** (new code fully exercised by its own
tests in the same PR — `TreatWarningsAsErrors` makes unreferenced members fail the build) · **Runtime-touching**
(edits a live class / App composition, **or ships a schema migration that `DatabaseMigrator` runs at host
startup**; guarded by the replay e2e + chunk tests).

| PR | Group | Tasks | Scope | Safety class | ~Diff |
|----|-------|-------|-------|--------------|-----:|
| **PR-A** ✅ `refactor(llm): provider-agnostic ILlmClient seam` | Contract | D5 (contract only) | Revise `ILlmClient`: `ModelId`→opaque `RouteKey`, `Failure(string)`→`Failure(LlmFailure)`, enrich `Success` with `LlmUsage`+`LlmCallInfo` (open `string ProviderId`), add `SchemaName`, declare `StreamAsync`. `LlmOptions`/`RouteOptions`/`ProviderOptions`/`ModelRate`. `CoachCadence` (incl. reserved `Strategy`). `FakeProvider` + trivial `LlmRouter`. No callers yet (existing `ILlmClient` has zero implementers/callers). | Additive + Dead-until-wired | ~480 |
| **PR-B** `feat(compute): tip-quality kernels + session-loss accumulator + strategy plumb` | D0 | B1, B2, M3-envelope, owner-plumb | **Append-only `telemetry.proto` edits** (protoc regen): new `CornerEvent` fields (`wheelspin_score`/`brake_overlap_steer_pct`/`steering_jitter`/`reason`), `LapEvent` temp-summary, `SessionEvent` M3 fields + fuel/tyre summary, new repeated `AggregatedLoss` message. New Pipeline kernels (`SimCoach.Pipeline/Kernels`); values written in `SimCoach.Reference` (`CornerEventBuilder`/`ComputeSession`); `SessionLossAccumulator` → bounded `aggregated_losses{corner_id,…}` (no `corner_name` — resolved at Coach layer) on `SessionEvent`; append-only mapper for strategy inputs (EngineMap/Tc/TcCut/Abs/pit-state). `normalized_car_position` already exists on the frame — **no gate work here (M7 lands in PR-G)**. Regenerates the Phase-2 event golden. **Edits live `ComputeService`.** | **Runtime-touching** | ~760 |
| **PR-C** `feat(coach): action registry + corner-name injection` | D1, D2a | D1 (+M5, M6), D2a (+m4) | `actionRegistry.json` (~24 actions, total-order `Priority`, M6 reference-free + `overdrove_entry` + gated catch-alls) + loader + `WhenClause` evaluator + template renderer (yields `RenderedParam`); `CoachOptions` (+ `RouteKeys` incl. strategy); valid-subset filter; `CornerNameMap` positional `.resx` + short + spoken-RU forms. No LLM. | Dead-until-wired | ~720 |
| **PR-D** `feat(coach): gold artifact builders` | D3 | D3 (+B1 fields, B2 losses, M3 envelope) | Per-cadence `GoldArtifactBuilder` + Gold records (incl. B1 scalars, `aggregated_losses`, per-sector aggregates, consistency, theoretical-best, `setup_hint`); determinism + privacy-serializer + `aggregated_losses` cap tests on synthetic events. | Dead-until-wired | ~640 |
| **PR-E** `feat(coach): prompt builder + per-cadence prompts + output schema` | D2b, D4 | D2b, D4 (+M4) | Versioned per-cadence system/few-shot resources (incl. `coach.system.debrief.v1.ru.txt` + sector/lap/no-PB/negative/debrief few-shots, number rule) + `PromptOptions`; per-request output-schema (enum=subset) + `SchemaName` + schema-lint + debrief `maxItems`; `LlmRequest` assembly. Few-shots double as golden fixtures. | Dead-until-wired | ~600 |
| **PR-F** `feat(llm): openrouter client + cost meter + circuit breaker + validate-on-start` | D5, D6, D7(breaker), B3 | D5, D6 (+cost-query, rate-card), D7-breaker, B3 | `OpenRouterProvider` (model-family-keyed `ISchemaTranslator`, SSE stub, failure classifier) registered under two ids (`openrouter-google`/`openrouter-anthropic`) + `LlmRouter`/CircuitBreaker/CostMeter decorator chain; debrief route → Sonnet 4.6 / Reasoning=Low / `MaxOutputTokens=2000`, DeepSeek registered-but-gated; `SqliteCostMeter` + `ICostQueryRepository` + `IRateCardQuery` + **migration `002_llm_usage_cost.sql`** (adds `provider`, `cached_input_tokens`) + migrator contiguity guard; per-provider breaker (isolation test); the **B3 `ValidateOnStart` checklist** + its six tests. Mocked `HttpMessageHandler` goldens; **no live network**. | **Runtime-touching** (migration runs at startup) | ~880 |
| **PR-G** `feat(coach): rule engine + coach service + tip sink` (dead-until-wired) | D7(rules), D8 | D7-rules (+M7 gate snapshot, strategy-zone), D8 | `RuleEngine` quiet zones + the **M7 gate-snapshot struct (`normalized_car_position` + corner-phase marker) that consumes the frame field** + reserved strategy-zone; `CoachService` orchestration + full `CoachTip` build (`ActionLabelShort`/`RenderedParam`/`Priority`+`Severity`/full+short+spoken name) + cadence-aware validation/retry/template (incl. deterministic `DebriefTemplate`) + drain-to-completion; `ICoachTipSink`/`ConsoleTipSink`; `CoachTipRepository`. **No host registration** — exercised by its own tests. | Dead-until-wired | ~640 |
| **PR-H** `feat(coach): host wiring + persistence + settings + e2e` | D9 | D9 (+migration 003, settings, declared repos) | `AddCoaching`/`AddLlm` DI + **stop-order insertion** (CoachService between Compute and Recorder) + gate-only frame subscription; **migration `003_coach_tips.sql`** (`rendered_param`/`priority`); `ISettingsStore` impl + `SqliteSettingsConfigurationSource` (model/budget re-bind) + declared `IReferenceQueryRepository`/`ISessionHistoryRepository` + reserved `debrief` columns (incl. `top_losses_json`); `IRateCardQuery` registration; replay e2e against `FakeProvider`. The single host-flip; LLM call still flag-off. | **Runtime-touching** (migration + composition at startup) | ~520 |

**Why 8, not 6:** the original 6-PR shape predates blockers B1/B2 and bundled the host-flip with a large
new component. Two deliberate splits fix that. **(1) PR-B** is a distinct *compute-layer* concern (Pipeline
kernels + `ComputeService` loss accumulation + append-only proto), owned by Reference/Pipeline, that is
**Runtime-touching** (it edits the live `ComputeService`, regenerates the Phase-2 golden, and triggers proto
codegen) and must land **before** the Gold builders (PR-D) and the registry validator (PR-F #4) that depend
on its fields. **(2) PR-G/PR-H** split the pure, large-test-surface `RuleEngine`+`CoachService` (dead-until-
wired, no host registration) from the **single irreversible host-flip** (`AddCoaching`/`AddLlm`, stop-order
insertion, migration `003`, settings/declared repos, e2e), so the wiring change is the smallest possible
diff and the new component does not inflate the blast radius of the one composition change. Otherwise the
structure is unchanged: **PR-A** is the only edit to *existing* contract code and lands alone; **PR-C/D/E**
are independent dead-until-wired libraries that parallelize cleanly; **PR-F** bundles the three agnostic
LLM-runtime pieces (client/cost/breaker) + the `002` migration its CostMeter needs + the B3 validator.
Migrations are numbered in **merge order** (`002` in PR-F precedes `003` in PR-H) so an incrementally-upgraded
DB — already at `user_version=2` after PR-F — still applies `003` (the migrator only runs versions
`> user_version`; the inverse numbering would silently skip the later table forever). If **PR-F** itself
overruns the ~600-line review ceiling, it splits cleanly (client+breaker | cost-meter+cost-query+migration+B3);
likewise PR-B (kernels | accumulator+proto) — ceilings, not the plan.

---

## Test strategy

- **No live network, ever (LOCKED #2):** the default `ILlmClient` route is `FakeProvider`; the real
  `OpenRouterProvider` is exercised only through a **mocked `HttpMessageHandler`** with committed golden
  request/response fixtures (the few-shot pairs double as fixtures). `Llm:Live` defaults off.
- **Compute kernels (B1) + loss accumulation (B2):** hand-built corner traces with known wheelspin/overlap/
  jitter/temp answers; degenerate laps return sentinels; `SessionLossAccumulator` over a synthetic stream →
  expected `aggregated_losses` (totals, `dominant_reason`, top-N cap); the regenerated Phase-2 e2e golden.
- **Determinism + golden fixtures:** synthetic domain events → Gold JSON goldens; Gold + subset → prompt
  goldens; mocked LLM JSON → expected `CoachTip` (incl. `RenderedParam`/`Priority`/short+spoken name). Same
  input → same output (no timestamps/UUIDs); total-order priority pins a deterministic `Take(5)` ordering.
- **Pure, clock-injected units:** `WhenClause` evaluator, valid-subset filter, `RuleEngine` (over a frame
  snapshot incl. `normalized_car_position`), and `CircuitBreaker` are pure over `(input, state, TimeProvider)`
  — **no sleep-based polling**.
- **Schema correctness:** schema-lint asserts `required == keys(properties)` for every strict schema; the
  debrief schema bounds `top_losses` to `maxItems`; **per-model-family** schema-acceptance fixtures (real
  HTTP shape) assert each family's translated schema is accepted — the Anthropic-via-OpenRouter fixture is
  the pre-pin guard that strict `json_schema` (or the forced-tool fallback) actually holds for Sonnet 4.6.
- **`ValidateOnStart` (B3):** one test per checklist item (rate coverage, route/cadence completeness,
  fallback acyclicity, registry-field-vs-Gold, positive timeouts/max-tokens, prompt-resource existence).
- **RU eval gate (m5):** a held-out RU eval defined as a *real* gate — an LLM judge + rubric + fixtures
  (incl. a **no-PB** case and a **debrief** case) + a numeric pass bar — runs per release (not in the
  no-network CI lane); the `gemini-2.5-flash-lite` real-time swap and any DeepSeek un-gating are blocked on
  it. **Prompt-caching decision (m5):** Anthropic prompt-caching is **enabled now on the static debrief
  prefix** (system prompt + few-shots are large and stable → meaningful cached-input savings, billed via the
  `CachedInputTokens` rate the CostMeter already carries); the real-time prefix is too small to cache
  (corner/sector/lap stay uncached). The gate may revise these thresholds but the default is decided, not
  punted. The "no accuracy upside" reasoning-off claim is design-asserted and *measured here*, not assumed (m3).
- **Privacy assertion:** the Gold-serializer test fails if any forbidden raw field (world coords, frame
  arrays, exact car id, raw fuel/strategy telemetry) appears in the outbound string; the gate-only frame
  snapshot is asserted never to reach a serialized artifact.
- **Migration safety:** `002` applied on a `001` DB adds exactly `provider`+`cached_input_tokens` (no
  duplicate `model_id`); `001→002→003` applies cleanly in order; the contiguity guard rejects a gapped set;
  `003` adds `rendered_param`+`priority`.
- **Replay e2e (macOS):** recorded MCAP → full spine → `CoachService`/`FakeProvider` → assert tip sequence +
  `coach_tips`/`llm_usage` rows + queryable session cost + final-`SessionEvent` tip survives shutdown +
  unchanged finalized counts.

## Risks / open questions

| Risk / question | Mitigation / decision |
|---|---|
| Provider outage in beta | Per-provider circuit breaker (`FR-037` 3/60 s/60 s) + route `FallbackRouteKey` + always-available RU template; `FakeProvider` for tests. |
| LLM response violates schema (~1–2% on Flash) | Tiny flat schema + small `enum` (valid subset only); cadence-aware retry (once for sector/lap/debrief, none for corner); then template. Post-validate before emit. |
| RU phrase quality on cheap models | The **RU eval gate (m5)** — judge + rubric + fixtures (no-PB + debrief) + numeric bar — per release; a template fallback is always available (real-time: the highest total-order action; **debrief: the deterministic `DebriefTemplate` rendered from `aggregated_losses`/per-sector aggregates**, so the session cadence also has a defined non-LLM path). `gemini-2.5-flash-lite` swap **and DeepSeek un-gating** are blocked on it. |
| **DeepSeek thinking+JSON corruption (vLLM #41132, M2)** | DeepSeek provider is **registered but config-gated OFF**; debrief default is `anthropic/claude-sonnet-4.6` (Reasoning=Low). Un-gate only after the upstream fix is verified and the RU eval passes. |
| Buffered call too slow / collides with next cadence event | Timeouts sized for real buffered latency (corner 2000 / sector 2500 / lap 3000 / debrief 8000 ms); corner tips are **exit-of-corner** advice; `DropOldest`; never retry on timeout; corner never retries on schema; breaker trips a degraded provider to template-only. |
| `when` clauses too tight → empty subset | Per-cadence **gated** catch-all with an explicit delta-floor (M6) so a real loss is covered while a clean corner stays silent; subset coverage asserted in golden tests. |
| Reference-free situations would otherwise be mute | M6 `ease_understeer`/`settle_oversteer` (`requires_reference=false`) let the overlay render a tip with `NoPbYet=true`. |
| Corner names missing (unbaked tracks) | Positional `поворот N` fallback (`.resx`) + short/spoken forms; `corner_id` retained for correlation. |
| Debrief model/price drift | All rates are config (`LlmOptions.Providers[…].Rates`), re-validated at composition; Sonnet 4.6 $3/$15, Haiku 4.5 $1/$5 (re-confirmed 2026-06-27); DeepSeek gated. CostMeter never hard-codes a price. |
| Real-time model deprecation (m1) | Default `gemini-2.5-flash-lite` (deterministic no-thinking latency, cheapest), `gemini-2.5-flash` the fallback; **`gemini-3.1-flash-lite` is the named eval-gated UPGRADE** (newer RU quality, ~$0.014/session) — promote only if the RU eval beats 2.5 Flash-Lite **and** `thinking_level: minimal` fits the 2000 ms budget (3.x cannot fully disable thinking). 3.5 Flash overkill real-time. |
| **FR-014 best-of-session-so-far reference deferred** | P3 emits no-delta tips flagged `NoPbYet`; the provisional best-of-session reference (resample the in-progress fastest clean lap onto the grid) is a Reference-layer feature deferred — recorded as an intentional divergence. |
| **M7 gate source** | **Resolved:** `normalized_car_position` (+ corner-phase marker) is added to the gate snapshot, so apex-window/straight/user-quiet-zone gates are real, not no-ops. |
| **Pit/strategy advisor** | **Seam reserved, delivery deferred:** `CoachCadence.Strategy` + a strategy quiet-zone + frame→Gold data plumb (fuel/wear/temps/pit-state/TC/ABS/engine-map) ship; **no Strategy tip is emitted in MVP**; timing model (main straight / pit-window approach, ~1 lap lead, threshold-driven, gated vs corner tips) recorded for a later race-craft phase. Engine-map/ABS/TC **advice** actions likewise reserved (data plumbed, actions not loaded). |
| Live UI reads not yet streamed (delta / sector-delta / speed-trace) | Noted as P5 compute extensions; **do not block P3**. The overlay's live delta-to-PB / in-progress sector delta is a **NEW per-frame channel** (per-frame reference-time lookup keyed on `normalized_car_position` against the loaded reference) — `SectorEvent` only yields *finalized* per-sector deltas, so P5 budgets to *build* this, it does not fall out of existing domain events. Nothing in P3 precludes it (M7 already adds the position field). Only the M7 `normalizedCarPosition` decision is a P3 call (taken). |
| Overlay tip dismissal / TTL | **Decided:** TTL/auto-dismiss is owned by the consuming sink/view (a P5 overlay constant), **not** the shared `CoachTip` DTO — so P5 cannot churn the record. `CoachTip` carries `GeneratedAtUtc` only. |
| Budget-default mismatch ($5 mockup vs $10 FR-072) | `budget.monthly_usd` default reconciled to **$5.00** (matches the shipped mockup); owner may override via settings. |
| Open: debrief delivery & streaming | Declared (`StreamAsync`) but **not built** in P3 — deferred to P6; the buffered debrief artifact/schema + Sonnet 4.6 route ship, live call flag-off. |
| Open: e2e fixture source | Reuse the Phase-2 synthesized multi-lap fixture (regenerated for the B1 fields); rebake against a live capture when Windows capture lands. |
| **FR-060 tyre-degradation element not real on ACC (surfaced by PR-B/D0 review, #2 — owned by Phase 6, not PR-B)** | ACC's `TyreWear` SHM channel is "Not used" (always 0), so `end_tyre_wear_pct` is an **honest-zero forward-compat field** (like `tyres_out`/`wheel_load`): the M3 fuel summary is real on ACC, the **tyre** half is not. **Assigned to Phase 6 (Post-Session Debrief)** — the debrief is first *delivered* to the user there, so the zero would otherwise become visible. Two candidate approaches recorded for that phase (neither validatable before live ACC stint captures exist): **B — pace-fall-off proxy** (clean-lap-time trend across a stint → `StintSummary.tyre_degradation_pct`, the proto field reserved for this, `[]` in MVP); **C — plumb indirect ACC channels** (tyre temp/pressure drift as raw estimator input, lands *with* B since it is dead code without a consumer). See `implementation-plan.md` Phase 6 for the full write-up. Until then FR-060's tyre-summary element is closed only for sims that report wear; the debrief copy states the ACC limitation rather than rendering a fake `0%`. |

## Draft docs to amend

- **`docs/02-architecture/adr/0004-llm-openrouter-gemini-deepseek.md`** — addendum: pricing re-validated
  2026-06-27 (Gemini 2.5 Flash-Lite $0.10/$0.40 real-time; **Sonnet 4.6 `claude-sonnet-4-6` $3/$15 = pinned
  debrief default, Reasoning=Low**; Haiku 4.5 `claude-haiku-4-5` $1/$5 middle ground). **DeepSeek gated OFF
  (vLLM #41132)** — drop the V3.2-vs-V4 default ambiguity. Note **reasoning OFF** real-time + **Low** debrief,
  per-cadence `Stream`/timeout (corner 2000/sector 2500/lap 3000/debrief 8000 ms), model IDs/rates are
  `IOptions` data, and the open `RouteKey`/`ProviderId` string model (no closed provider/cadence vocab).
  Record the OpenRouter-slug-vs-canonical-Anthropic-id distinction.
- **`docs/02-architecture/action-registry.md`** — add the Phase-3 notes: corner-name injection from the
  first-party baked dataset (ADR-0014) in `PromptBuilder` (+ short/spoken forms); registry is **JSON**
  (`System.Text.Json`); **priority is a total order** (M5); the M6 reference-free + `overdrove_entry` + gated
  catch-all actions; the B1-backed wheelspin/brake-overlap/steering-jitter + lap-cadence temp actions; and
  that the debrief schema lists `setup_hint` in `required` (nullable) with `top_losses.maxItems`.
- **`docs/02-architecture/adr/0010-corner-model-from-vendored-landmarks.md`** — mark sourcing **superseded by
  ADR-0014**; the load-bearing principle (names at the prompt layer, never in compute) still holds.
- **`docs/03-functional/functional-requirements.md`** — note FR-014's best-of-session fallback is **partially
  deferred** (no-PB-yet label ships; provisional reference deferred); FR-031/061's "OpenRouter" wording is one
  implementation behind the provider-agnostic seam; FR-060's debrief loss-attribution is now sourced by
  `aggregated_losses` (B2) and its fuel/tyre-summary element by the derived session summary (M3); record the
  reserved `CoachCadence.Strategy`/pit-advisor and engine-map/ABS/TC data-plumbed-but-advice-deferred
  decisions; reconcile FR-072's `$10` cap with the **`budget.monthly_usd=$5.00` default** (mockup-matching;
  user-overridable).
- **`docs/03-functional/ui-client-requirements.md`** — keep in sync: the `CoachTip` fields (incl.
  `ActionLabelShort`, full `CornerName`, derived `CoachSeverity`), `ICoachTipSink`, `ICostQueryRepository` +
  `IRateCardQuery` + `ISettingsStore` (implemented P3, with the `SqliteSettingsConfigurationSource` read-path),
  `IReferenceQueryRepository`/`ISessionHistoryRepository` (declared P3), `coach_tips`(`rendered_param`/`priority`)/
  `llm_usage` migrations, and the reserved `debrief` columns (incl. `top_losses_json`) are all delivered/declared
  by the PR table above.
- **`docs/05-implementation/implementation-plan.md`** — fix the stale Phase-3 PromptBuilder bullet ("vendored
  CrewChief landmark file" → "first-party baked `cornerGeometry.json` per ADR-0014"); scope the OpenRouter
  "streaming" bullet to the **debrief only (P6)** (real-time is buffered); update the Phase-3 header from
  **"6 PRs, A–F" → "8 PRs, A–H"** and **"~20 actions" → "~24 actions"**; add the B1 compute-kernel + B2
  loss-accumulator + reserved-Strategy-seam items to the Phase-3 checklist.