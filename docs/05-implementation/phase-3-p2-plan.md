# Phase-3 P2 pack — completeness, robustness, observability

*Status: **implementable** — owner decisions recorded (see below), plan-level must-fixes applied,
validated Strict→Defence→Judge. Branch: `feat/phase-3-p2`, stacked on the P1 tip (off `main`).
Each of the nine items is exactly **one commit**. Scope constraint: **near-proto-free** — no `.proto` /
`SimCoach.Contracts` change **except one owner-ratified additive scalar**: Wave-A review found that M19's
`trail_brake_absent` cold-start action fired on flat/lift-only corners (`trail_brake_pct_self=0` conflates
"braked-but-didn't-trail" with "didn't-brake-at-all"), and a brake-presence signal can only reach the
Coach clause engine over the `CornerEvent` proto. The owner ratified adding `CornerEvent.peak_brake_pct = 17`
(a small backward-compatible additive scalar, also useful for future brake-lockup detection, M33) to gate
the action on `peak_brake_pct gt 0.1`. All other items stay proto-free. Where an item's fuller version
would need further proto/contract change, that version is **not baked**; it is named and deferred to the P3
proto/compute pack (M35/M36 neighbourhood).*

## Scope

P0 (detection truthfulness) and P1 (coaching quality — M7 abstain, M9, M10 cadence-governor, M18 gate)
are shipped (PR #26 merged; PR #27 open). P2 is the closure wave: it fills completeness gaps, hardens
the routing/ledger, and adds observability, **without** re-opening the telemetry contract. The LLM stays
a selector+phraser; every telemetry decision remains algorithmic.

Everything below stays inside `SimCoach.Coach` / `SimCoach.LLM` / `SimCoach.Pipeline` /
`SimCoach.Reference` / `SimCoach.Storage` / `tests/SimCoach.RuEval` + `SimCoach.App/appsettings.json`.
No `ComputeSession` relocation, no Contracts diff, no `GoldArtifactBuilder` statefulness.

**Conventions (enforced, `TreatWarningsAsErrors`):** records over classes, `init`-only setters,
`IReadOnlyList`/`IReadOnlyDictionary` on public surfaces; private fields `_camelCase` **including
`private static readonly`** (only `const` is PascalCase); `var` rules IDE0007/0008 (apparent-type →
`var`, non-apparent → explicit); no magic numbers (named `const` or `IOptions`); `System.Text.Json`
only; one public type per file; Russian only in prompts/`.resx`, code identifiers and comments English.
Each commit builds clean under `dotnet build` + `dotnet format --verify-no-changes` + full `dotnet test`.

The nine items and what each closes:

| Item | Area | One-line intent | Proto risk |
|------|------|-----------------|------------|
| M26 | compute kernel | Gate `BalanceKernels` to steady-state + normalise understeer/oversteer to `[0,1]` via a scale-free ratio | none |
| M22 | LLM routing | Narrow single-shot router fallback trigger + wire a real `debrief` fallback route | none |
| M28 | LLM/Storage observability | Persist `reasoning_tokens`, echo the refusal reason on retry, per-family robustness | none |
| M30 | eval harness | Env-gated advisory A/B RU-quality shadow-harness over real Gold events | none |
| M20 | coach debrief | Session Gold field-set groundwork + surface session metrics in the deterministic debrief | none |
| M19 | coach registry | Reference-free (off-track + absolute trail-brake) corner actions — **partial** cold-start close | line/min-speed deferred → P3 |
| M21 | coach registry | `corner_catch_all`: gloss `reason` instead of raw ms, dedup same-family | none (reuses existing `reason` field) |
| M31 | coach schema | Bounded `confidence` enum (high/low) on real-time tips + logging (observe-only) | none |
| M32 | coach realtime | Dedup identical corner tips with cross-lap memory | trend injection deferred → P3 |

---

## Решения владельца (P2, 2026-07-04)

These are **binding** and recorded verbatim; each task section below references them. They resolve every
driver-audible / cost-affecting / irreversible choice that the draft left open.

- **M21 (owner):** `corner_catch_all` = **Gloss mode** — when it leads, replace the raw ms with the RU
  cause from the `reason` field via a shared gloss helper (debrief + realtime, reusing the 5 existing
  resx keys); **silent** when `reason` is empty/`slower`. Same-family dedup = **targeted strip** of
  catch-alls (rank ≥ `CatchAllRank`) when a non-catch-all same-cadence action survives — **no schema change**.

- **M19 (owner):** **two families** — `off_track` "ran wide" (thresholdless) + an absolute low-trail-brake
  tier (conservative provisional thresholds, in `actionRegistry.json`; owner ratifies exact phrasing/values
  later). Line-deviation + absolute min-speed **deferred to P3** (proto).

- **M22 (owner):** debrief fallback model = **`anthropic/claude-haiku-4.5`** (same provider — add its
  `openrouter-anthropic` rate card; document the no-outage-redundancy limitation). `FallbackRouteKey`
  **only on debrief**; realtime keeps M7 abstain. Circuit knobs keep defaults (dev-tier `IOptions` + XML-doc).

- **M32 (owner):** `RepeatSuppressionLaps = 2` (0 = off), **Tier-1 user-facing** in `CadenceOptions`
  (XML-doc + `EnsureValid`) + always-on within-lap idempotency; **High bypasses dedup** (explicit
  `!highSeverity` conjunct); key on exact `action_id` now (align to M21 family later); corner identity via
  an `in TipIdentity` struct; suppression-only (trend injection deferred to P3).

- **Defaults (owner-ratified via recommendation):**
  - **M26** steady-state gate = brake + longitudinal-g (degrade to brake-only if `GForceG` absent/zero);
    named `private const`, **not** `IOptions`. Normalisation formula + threshold recalibration are ratified
    **before** the M26 commit lands (see M26 §Ratified-before-commit).
  - **M20** debrief surfaces consistency-stddev + theoretical-best-gap as grounded numbers with neutral RU
    resx labels, **template-only** (no LLM schema field, no `coach_tips` column).
  - **M28** hard-fail at startup on a Gemini-debrief misconfig; defer the explicit `thinking_budget=0`
    knob (persist + verify first); retry echoes a terse RU `"Причина отказа: <reason>"` with a small EN→RU
    map; enrich the `Transport` failure message with `finish_reason` (no `ILogger` injection).
  - **M30** gemini-only first cut (2.5 vs 3.1), 5 fixtures, **advisory-only** `[Fact]`; routing-switch is a
    separate owner follow-up; reuse `SimCoach.RuEval`.
  - **M31** confidence on all realtime cadences, **observe-only** (never affects emit/silence/severity this
    commit); RU semantics high=`Gold clearly supports` / low=`ambiguous`; missing→high; **log-only** (no DB column).

---

## Принцип конфигурируемости

Two tiers, identical to the P1 discipline:

1. **User-preference knobs → one coherent user-facing (Tier-1) `IOptions` surface** — XML-doc + `EnsureValid`,
   UI-ready (slider/toggle-shaped). Shapes what the driver hears.
2. **Internal / correctness + judge/model knobs → dev (Tier-2) `IOptions`**, not user sliders — XML-doc +
   `EnsureValid`, but never surfaced as a live control.
3. **Ambiguous → user-facing + a note** explaining the tension.

P2 tier assignments:
- **Tier-1 (user-facing):** `CadenceOptions.RepeatSuppressionLaps` (M32) — it directly shapes chattiness.
- **Tier-2 (dev):** M26 gate/scale constants (named `const`, correctness), M19 registry thresholds
  (in-registry data like the other 24 actions — internal calibration), M22 circuit knobs, M31
  `RequestConfidence` flag + high/low semantics (correctness/model heuristic), M30 `AbHarnessOptions`.
- M31 confidence semantics and M26/M19 thresholds are **internal** (correctness), not user sliders.

---

## Под-волны реализации

The nine commits split into two review-sized sub-waves along the *coaching-content/detection* vs
*LLM/routing/robustness* fault line. **Each task = one commit**, sequenced by shared files. Each wave
ships as its **own PR** after its **own Strict→Defence→Judge diff review**.

### Wave A — coaching content / detection / registry: **M26 → M19 → M21 → M20**

`actionRegistry.json` is shared by M26 (balance thresholds), M19 (new cold-start actions), and M21
(`corner_catch_all` rewrite + `ValidSubset` dedup) → **strictly sequential**. **M26's normalisation
formula + threshold recalibration are ratified before its commit** (see M26 below) — so wave A does
carry a driver-audible decision (the recalibrated balance-action fire-rate), and that decision is
locked before code lands. M20 (debrief surfacing) lands last; it is template-only and does not touch
the registry.

### Wave B — LLM / routing / robustness / coach dedup: **M22 → M28 → M30 → M31 → M32**

`CoachService` + `OutputSchema` real-time accept path is shared by M31 (confidence `out`-threading) and
M32 (corner-identity threading) → **sequential** (M31 before M32). M22/M28 touch routing/appsettings and
the LLM/Storage ledger; M30 is test-project-only and independent. **M31 builds on the shipped M7**
(confidence is only meaningful with the abstain gate); **M32 composes with the shipped M10** cadence-governor
(it must **not** duplicate M10's per-lap cap or cooldowns).

---

## Wave A

### 1. M26 — `BalanceKernels`: steady-state gating + normalisation

*`fix(compute): gate BalanceKernels to steady-state and normalise understeer/oversteer (M26)`*
Source `[СИС#9]` (`phase-3-acceptance-addendum.md:76`).

Today `BalanceKernels.Analyze` scores every frame with `|steer| > 0.05 rad` — including braking and
throttle-application phases — using the **raw** `wheel_slip` magnitude (ACC range ~0..12.37). Under
braking the front axle carries load and slips more, so a neutral car reads as understeer; that
un-normalised score is compared against the `0.6`/`0.7` action thresholds and folded into the
`[-1,1]`-clamped `understeer_trend`. M26 (1) scores only steady-state mid-corner frames and (2)
normalises the per-frame front/rear slip delta into `[0,1]` **before** thresholds and clamp, so those
bars finally mean what they claim.

**Ratified-before-commit (owner default, wave-A gate):**
- **Normalisation FORMULA = scale-free ratio `|front − rear| / (front + rear)`** — inherently in `[0,1]`,
  **no magic scale constant** to tune, degrades to 0 when both slips are 0. Applied per-frame *before*
  accumulation, so `understeerSum/corneringFrames` is a mean of `[0,1]` terms and is bounded `[0,1]` by
  construction; the downstream `[-1,1]` `understeer_trend` clamp becomes a backstop, not the primary bound.
- **Threshold recalibration in lockstep** — a raw-slip `0.6` and a ratio `0.6` are different bars, so the
  four gated actions (`less_trail_brake` / `wider_entry` / `ease_understeer` / `settle_oversteer`,
  currently `0.6`/`0.7`) are **recalibrated in the same commit**. The net firing-rate change is
  **intended and verified against recorded MCAP replays** (dev-loop replay of the authoritative sessions),
  not silently shifted. This recalibration lands in `actionRegistry.json` **before** M19/M21 rebase onto it.
- **Steady-state gate = brake + longitudinal-g:** skip a frame when `BrakePct > BrakeQuietMax` **or**
  `abs(GForceG.z) > LongGQuietMax`. If `GForceG` is null/zero (ACC omits it), **degrade to brake-only**
  (the existing `frame.GForceG is null` guard pattern at `MedianCenterlineBuilder.cs:68` is the precedent).
- **Constants** are named `private const` with why-comments in `BalanceKernels` (house convention,
  `ComputeOptions.cs:4-5`; precedent `CorneringSteerThresholdRad`, `BalanceKernels.cs:17`), **not** `IOptions`.

- Touches: `BalanceKernels.cs:33-63` (gate widens, per-frame ratio before summation),
  `KernelResults.cs:36-45` (XML-doc: new steady-state scope + `[0,1]` normalisation),
  `CornerEventBuilder.cs:49,66-67,81,97,148` (call site — no signature change),
  `ComputeSession.cs:175-176,268-270` (clamp/accum become backstops), and the four balance thresholds in
  `actionRegistry.json:45-48,92-104,138-153,188-203`. Also update `GoldTestData.cs` /
  `SessionLossAccumulatorTests.cs` if they assert specific magnitudes.
- Proto-free: `understeer_score`/`oversteer_score` (CornerEvent 11/12) and `understeer_trend`
  (SessionEvent 11) already exist; only the computation + JSON re-tuning + tests change.

**Acceptance (pinned):** `ComputeKernelsTests` — **a frame under heavy braking (high `BrakePct` / high
`abs(Glong)`, front>rear slip) no longer contributes to `understeerSum`** and yields `UndersteerScore == 0`
where today it returns a positive score (the SIS#9 regression). Plus: steady-state understeer still scores
and lands in `[0,1]`; an extreme raw delta (front 12, rear 0) produces a score `≤ 1`; an all-braking window
returns `{0,0}`; `understeer_trend` stays `[-1,1]` and braking-heavy corners no longer bias it positive.
Update `Balance_scores_separate_understeer_from_oversteer` (`:103`) to the ratio expectation; extend
`FrameWithSlip` with a brake/g-force overload.

### 2. M19 — Reference-free tier (partial cold-start close)

*`feat(coach): reference-free corner actions for cold-start laps (M19)`*
Source `[СИС#8]` (`addendum:149`) / `[ПД#H]`.

On a driver's first clean lap on a fresh track/car/weather triple there is no persisted reference, so
`ActionRegistry.ValidSubset` filters out every `requires_reference: true` action, leaving only **5 of 15**
corner actions live (pure car-control symptoms). The first laps get zero line/wide/trail-brake coaching.
M19 adds new corner actions gated `requires_reference: false` that trigger on **already-present, self-only
(absolute) Gold channels**.

**Scope (owner): two families.**
- **`off_track` "ran wide"** — clause `{ off_track eq true }` (thresholdless). `off_track` is non-nullable
  on every corner event and today is used only as a *guard* (`eq false`), never a positive trigger.
- **Absolute low-trail-brake tier** — `trail_brake_pct_self` (always present, `GoldCornerEvent.cs:21`) with
  **conservative provisional thresholds** in `actionRegistry.json`; the owner ratifies exact phrasing/values
  later.

**M19 only PARTIALLY closes cold-start.** Reference-free line deviation (`racing_line_deviation_m` is nulled
without a reference — `GoldArtifactBuilder.cs:35`; the median centerline is offline-only) and an absolute
min-speed channel both need new runtime/proto plumbing and are **deferred to P3** (proto/compute pack).
M19 ships the `off_track` ran-wide proxy + absolute trail-brake as the interim.

- **No code change to the gate** — `ValidSubset` (`ActionRegistry.cs:100`,
  `gold.HasReference || !a.RequiresReference`) already admits reference-free actions cold-start; M19 is
  additive registry data. `GoldFieldNames._corner` (`:14-20`) already contains every referenced channel, so
  the fail-fast loader passes with no edit. New actions need globally-unique `(phase, rank)`
  (`ActionRegistry.cs:79-83` throws on duplicate). Thresholds stay **in-registry** like the other 24 actions
  (Tier-2 internal calibration). Menu stays capped at `MaxActionsInMenu = 5` (`CoachOptions.cs:27`).
- **Registry ordering:** lands **after** M26 (reconciled balance thresholds) and **before** M21 (which
  rewrites `corner_catch_all` and adds the `ValidSubset` dedup these new actions must interact with cleanly).

**Acceptance (pinned):** `ActionRegistryFilterTests` — **on a `HasReference=false` lap-1 corner event
exhibiting an off-track (or absolute trail-brake) symptom, `ValidSubset` returns ≥ 1 reference-free action**
where today the menu would be car-control-only/empty; reference-only actions stay excluded; menu length
≤ `MaxActionsInMenu`. Plus: **zero behaviour change on referenced laps** (regression on the authoritative
run); registry still loads (unique ids + priorities, all fields in `_corner`).

### 3. M21 — `corner_catch_all`: gloss reason, drop raw ms, dedup same-family

*`feat(coach): gloss corner_catch_all reason, drop raw duration, dedup same-family (FR-060)`*
Source `[ПД#G]` / `[LLM n35]`.

Stop `corner_catch_all` voicing a bare millisecond count ("В {corner} отклонение около 250 мс") and
instead **name the cause** via the already-populated `reason` field glossed to RU, or **stay silent**;
additionally **dedup the catch-all out of the menu** when a specific same-corner action fired.

**Proto verdict: PROTO-FREE (confirmed).** `reason` is an existing, fully-populated `CornerEvent.reason`
field (`telemetry.proto:105`) that already crosses the Gold seam (`GoldArtifactBuilder.cs:45`,
`GoldCornerEvent.cs:30`, `CornerGoldView.cs:59`) and is a registered scalar (`GoldFieldNames.cs:19`); it is
"dead" only in that no action consumes it. The RU gloss already exists as the 5 `Reason_*` resx keys used by
`DebriefTemplate.ReasonRu` (`CoachStrings.resx:83-98`, `DebriefTemplate.cs:54-55`). No proto change.

**Owner decision — Gloss mode + targeted strip, no schema change:**
1. **Shared gloss helper.** Lift `DebriefTemplate.ReasonRu` into an internal `ReasonGloss.ToRu(reason)`
   (`SimCoach.Coach`) mapping a closed-set `reason` → `CoachStrings.Get("Reason_" + reason)`; **debrief and
   realtime share this one taxonomy and one fallback** (reuses the 5 existing keys — no new resx keys).
2. **New `ReasonRu` param transform** — add the enum value (`ParamTransform.cs`), the `MapTransform` case
   (`ActionRegistry.cs:231-238`), and the `PhraseRenderer.RenderValue` branch (`:33-64`). It is a **string
   gloss, NOT quantitative**: it must NOT set `RenderedParam` (treat like `Transform.None` for chip
   promotion, guard at `PhraseRenderer.cs:24`) so the overlay chip stays number-or-nothing.
3. **Rewrite `corner_catch_all`** (`actionRegistry.json:236-252`): drop the raw `{loss}` ms param; add
   `{reason}` (from `reason`, transform `reason_ru`); `phrase_template_ru` = e.g. `"В {corner} теряешь:
   {reason}."`. **Silent when `reason` is empty/`slower`** (gate the action out) — the deterministic path
   emits nothing rather than a vague gloss.
4. **Targeted same-family strip** in `ValidSubset` (`ActionRegistry.cs:96-104`): when a non-catch-all
   same-cadence action survives, strip catch-alls (rank ≥ `CatchAllRank`, `CoachOptions.cs:38`) from the
   menu before `Take`. Pure LINQ, **no schema change**, never drops the last item.
- **M7 interaction (must stay pinned):** when the catch-all is deduped, the lead rank < `CatchAllRank`, so
  `AllowsAbstain` correctly does not offer abstain; when the catch-all is genuinely the only action it stays
  the lead and M7 abstain still applies. Both branches test-pinned so M21 doesn't silently disable M7.
- **Registry ordering:** lands last of the three registry items (after M26 thresholds, M19 additions).

**Acceptance (pinned):** `PhraseRendererTests` + `ActionRegistryFilterTests` — **a catch-all with an
empty/`slower` reason stays silent and renders no ms chip, while a real `reason` (e.g. `late_throttle`)
renders the RU gloss** and `RenderedParam` stays empty; and **the catch-all is stripped from the menu when a
specific same-corner action passes**, survives when it is the only passing action, and the menu never empties.
Plus: no realtime tip ever contains a bare "≈ N мс" from `corner_catch_all`; debrief and realtime resolve
identical RU for the same reason key (one helper).

### 4. M20 — Session field-set groundwork + session metrics in the deterministic debrief

*`feat(coach): surface session metrics in the deterministic debrief (M20)`*
Source `[СИС#7]` / `[ПД §2]` (backlog `phase-3-master-backlog.md:99`).

Two deliverables:

1. **Session Gold field-set groundwork.** Today `GoldFieldNames.For(Session)` **throws** (`:43`). This
   deliverable is scoped as **groundwork tied to the open Session-Gold-view question**: add a `_session`
   `FrozenSet<string>` (the flat scalar fields of `GoldSessionPayload` — `lap_count`, `clean_lap_count`,
   `pb_time_ms`, `average_lap_ms`, `understeer_trend`, `consistency_stddev_ms`, `theoretical_best_gap_ms`,
   `has_reference`; excluding non-scalar aggregates) and route `CoachCadence.Session` to it in the `For`
   switch. **Keep `Strategy` throwing** (no Gold payload exists for it). This does *not* introduce a full
   `SessionGoldView` adapter — that remains a P3 question; M20 lays only the catalog groundwork it needs.
2. **Surface session metrics in the deterministic templated debrief.** The metrics already reach
   `GoldSessionPayload` (`GoldArtifactBuilder.cs:82-96`) but the debrief JSON only emits
   `top_losses`/`top_priority`/`setup_hint` (`DebriefTemplate.cs:32-39`). **Owner default: template-only** —
   `DebriefTemplate.BuildJson` renders **consistency-stddev + theoretical-best-gap** as grounded numbers with
   **neutral RU resx labels**; **no LLM schema field, no `coach_tips` column**. Nullable metrics (`<2` clean
   laps → null consistency; `<1` clean lap → null gap) are **dropped, not zero-filled**. Numbers come only
   from Gold; no thresholds, no magic numbers. Fixed render order + `InvariantCulture` so the byte-stable
   golden test stays deterministic.

**Proto-free confirmed** — `average_lap_ms`/`consistency_stddev_ms`/`theoretical_best_gap_ms` already exist
(`telemetry.proto:145-163`) and are computed/published (`ComputeSession.cs:189-193,508-531`).

- Touches: `GoldFieldNames.cs:14-45` (add `_session`, extend `For`), `DebriefTemplate.cs:16-40` (emit metric
  fields, null-drop), `CoachStrings.resx:99-105` (neutral RU metric labels). **Touch-list fix:**
  `tests/SimCoach.Coach.Tests/GoldFieldNamesTests.cs:43-62` — the `For_throws_for_cadences_without_a_set`
  theory **currently pins `Session` throwing**; it must be split so **`Strategy` still throws, `Session` no
  longer**. `OutputSchema`/`TipValidator`/prompt/`coach_tips` are **not** touched (template-only default).
- **Word-cap safety:** metrics are a separate template field, **not** appended to `top_priority`, so they
  cannot trip `DebriefMaxWords` (`TipValidator.cs:122`).
- **Ordering:** lands after M28 settles the `TryAcceptDebrief`/debrief-validation signature (see wave B note);
  within wave A it is last and touches no registry file. *(Note: M28 is in wave B; M20's dependence is only
  on the debrief-validation signature, which M20 does not itself modify under the template-only default, so
  the cross-wave coupling is informational, not blocking — see Open questions.)*

**Acceptance (pinned):** `GoldFieldNamesTests` — **the drift-guard test replaces the current
`For(Session)`-throws pin**: `For(CoachCadence.Session)` now returns a non-empty collision-free scalar set
equal to the scalar surface of `GoldSessionPayload` (guards catalog/record drift), while `For(Strategy)`
still throws. Plus `DebriefTemplateTests`: with ≥2 clean laps + a clean PB the debrief JSON contains the
consistency + theoretical-best metrics; with `<2`/no clean lap those fields are **absent** (null-drop);
`BuildJson(x)==BuildJson(x)` still holds; RU text is resx-sourced.

---

## Wave B

### 5. M22 — Narrow single-shot router fallback + real `debrief` fallback route

*`feat(llm): broaden router fallback trigger and wire debrief -> cheaper model (M22)`*
Source `[LLM n17/n18/n23]`.

Today a live-route failure only falls back when the provider's breaker is **already open**
(`LlmRouter.cs:52` matches exactly `LlmFailure.CircuitOpen`); the first 1-2 real failures surface as a plain
`Failure` and the debrief is lost before the breaker trips (threshold 3 in 60 s). No route sets
`FallbackRouteKey`, so the fallback path is dead config.

**MUST-FIX — the router trigger is NARROWER than the breaker's, and does NOT reuse `IsTripWorthy`.**
The breaker's `CircuitBreaker.IsTripWorthy` (`:157-161`) is `Timeout | RateLimited | Transport |
ServerError{>=500}` and **stays as-is** (the breaker keeps its wider set). The router fallback uses a
**separate, narrow, single-shot trigger**:

> **Router falls back on: `Timeout` | `Transport` | `ServerError{StatusCode >= 500}` | `CircuitOpen`.
> It EXCLUDES `RateLimited`.**

Rationale: a `429` on the same provider cannot be fixed by an immediate retry on that same provider; the
correct behaviour is to **honour `RetryAfter` by NOT falling back** (`LlmFailure.RateLimited` carries
`RetryAfter`). `SchemaViolation`/`Auth` stay non-fallback by construction (bad schema / bad key won't be
fixed by a cheaper model). This is a **new predicate**, e.g. `LlmFailurePolicy.ShouldRouterFallback(LlmFailure)`
in its own one-type file — distinct from the breaker's `IsTripWorthy`, so the two policies are intentionally
different and neither silently drifts into the other.

**Owner decision — `anthropic/claude-haiku-4.5`, debrief-only fallback.**
- Add a `debrief_fallback` route under `openrouter-anthropic` pointing at **`anthropic/claude-haiku-4.5`**,
  with a **tighter `Timeout`** than debrief's 20 s, `MaxOutputTokens` sized for the same debrief schema,
  `Reasoning: "Off"`; add that model's **rate card** under `openrouter-anthropic` `Rates`; set
  `"FallbackRouteKey": "debrief_fallback"` on the `debrief` route. `debrief_fallback` itself has **no**
  `FallbackRouteKey` (acyclicity check `LlmStartupValidator.cs:59-82` stays green).
- **`FallbackRouteKey` only on debrief**; realtime keeps M7 abstain (no realtime fallback route).
- **Circuit knobs keep defaults** — dev-tier `IOptions` (`CircuitBreakerOptions`, already `EnsureValid`);
  add/keep XML-doc, no user-facing slider, no value change required.
- **Documented limitation — same-provider fallback ≠ redundancy:** `debrief_fallback` shares the same
  breaker key (`CircuitBreakerProvider.cs:23` keys by `route.ProviderId`), so a genuine `openrouter-anthropic`
  outage fails both attempts; the win is real only for a slow/overloaded-primary or a transient primary
  failure. Topology/model redundancy is a separate follow-up. This limitation is stated in the route's
  XML-doc/appsettings comment.

- Touches: `LlmRouter.cs:44-62`, new `LlmFailurePolicy.cs` (router predicate), `appsettings.json:64-70`
  (routes), `:80-86` (`openrouter-anthropic` rates), `:95-99` (circuit — comment/doc only).
  `LlmStartupValidator` `#1` (rate coverage) + `#3` (acyclicity) cover the new route for free — land route +
  rate + `FallbackRouteKey` together or `ValidateOnStart` trips at boot.
- Offline: with `Llm:Live=false` every route resolves to `fake`; chain tests must set `Live=true`
  (`LlmRouterChainTests.cs:98-104` pattern).

**Acceptance (pinned):** `LlmRouterChainTests` — **`Failure(Timeout)` with `FallbackRouteKey` set falls back
once and can yield a debrief; `Failure(RateLimited)` does NOT fall back (returned as-is, honouring
RetryAfter); `Failure(ServerError, 503)` falls back; `Failure(ServerError, 400)` does not; `SchemaViolation`
/`Auth` do not; no `FallbackRouteKey` → original failure returned.** Existing CircuitOpen fallback +
no-fallback cases still pass. `LlmStartupValidator` fixture with the new route+rate validates clean; a
missing rate or dangling target fails.

### 6. M28 — Persist `reasoning_tokens` + retry-reason echo + per-family robustness

*`feat(llm): persist reasoning_tokens + retry-reason echo + per-family robustness (M28)`*
Source `[LLM n22/n6/n13]`.

Four small observability/robustness gaps, none of which change cost math or what a driver hears:

1. **Persist `reasoning_tokens`** per `llm_usage` row — the provider already reads it
   (`OpenRouterProvider.cs:248-252`) and cost already bills it at output rate (`CostCalculator.cs:19`), but
   the count is dropped before the DB, so "thinking is off" cannot be confirmed from data. This is an
   **observability hole, NOT a cost undercount** (`cost_usd` is already correct). New migration
   `005_llm_usage_reasoning_tokens.sql` (`ALTER TABLE llm_usage ADD COLUMN reasoning_tokens INTEGER NOT NULL
   DEFAULT 0;` — mirrors `002`), `LlmUsageRow.ReasoningTokens`, INSERT binding, cost-meter mapping
   (`SqliteCostMeter.cs:45-58`). Migration `005` must be the sole next version (contiguity,
   `DatabaseMigrator.cs:92`).
2. **Confirm thinking-off from data** — a verification query once the column exists (`Reasoning:Off` routes
   should record 0). **Owner: defer the explicit `thinking_budget=0` knob** — persist + verify first; the
   knob is a separate later decision, not baked here.
3. **Retry prompt echoes the refusal reason** (n6/n14). **Owner: retry echoes a terse RU
   `"Причина отказа: <reason>"` with a small EN→RU map** of the validator strings (e.g. `"phrase_ru exceeds 8
   words"` → RU), appended to the retry system prompt. Realtime: pass the already-captured `rejection`
   (`CoachService.cs:279→292`). Debrief: **widen `TryAcceptDebrief`** (`:478-495`, currently `out _, out _`)
   to surface the `failure` string and append at `:418`. Retry scope unchanged (`IsRetryable`,
   `:499-504`): corner never retries — helps sector/lap/debrief only.
4. **Per-family robustness** (n13): **Owner: hard-fail at startup** in `CoachStartupValidator` when the
   resolved debrief-route model is `SchemaFamily.Gemini` (its `maxItems` is stripped, leaving only post-parse
   `TipValidator` enforcement) — detect via `ISchemaTranslatorSelector.For(modelId).Family`, no hardcoded
   model list. Add an intentional-strip comment + `SchemaTranslatorTests` assertion on
   `GeminiSchemaTranslator` (`maxItems` in `_bannedKeywords`, `:21`). **Enrich the no-content `Transport`
   failure message with `finish_reason`** (`OpenRouterProvider.cs:187-192`; already parsed at `:194-197`) —
   **message-string enrichment, no `ILogger` injection** (keeps the Ring-2 adapter logger-free); degrade
   gracefully when `finish_reason` is null.

- Touches: `SqliteCostMeter.cs`, `Rows.cs:39-52`, `LlmUsageRepository.cs:29-34`, new migration `005`,
  `CoachService.cs:292,418`, `TipValidator` (widen debrief signature; realtime strings `:47/57/63/69`),
  `CoachStartupValidator.cs` (Gemini-debrief hard-fail guard), `GeminiSchemaTranslator.cs`,
  `OpenRouterProvider.cs`. **`cost_usd` byte-for-byte unchanged** (`CostCalculator` untouched).
- **Debrief-path overlap with M20:** both touch the debrief path; M28 settles the widened `TryAcceptDebrief`
  signature. (Cross-wave: M28 is wave B, M20 wave A. Under M20's template-only default M20 does not modify
  `TryAcceptDebrief`, so there is no hard blocking dependency — see Open questions.)

**Acceptance (pinned):** `LlmUsageRepositoryTests` — **an `llm_usage` row round-trips `reasoning_tokens`**
(insert 40, read back 40); `SqliteCostMeterTests` — a non-zero `Usage.ReasoningTokens` lands in the row with
`cost_usd` unchanged vs baseline. Plus: `OpenRouterProviderTests` surfaces `finish_reason` on the no-content
failure; `SchemaTranslatorTests` pins `maxItems` stripped; `CoachStartupValidator` **hard-fails** on a
Gemini debrief route and passes for the shipped `anthropic/claude-sonnet-4.6`; a validation-failing retry
carries the RU reason.

### 7. M30 — Advisory A/B RU-quality shadow-harness

*`test(rueval): add advisory A/B one-liner model shadow-harness (M30)`*
Source `[LLM n19]`.

Env-gated, **advisory-only** harness that runs the *same* committed Gold events through several candidate
one-liner models, has the existing `anthropic/claude-sonnet-4.6` judge score each on the 5-dim rubric, and
reads the per-call `llm_usage` ledger for cost/latency — so the corner/sector default is chosen from data.
Sibling of the shipped M18 gate: same fixtures, judge, rubric, but fans the request across N candidate
routes via `fixture.CandidateRequest with { RouteKey = candidateRouteKey }` and tabulates.

**Owner decision — gemini-only first cut, 5 fixtures, advisory-only, routing-switch is a separate follow-up.**
- First cut compares **`gemini-2.5-flash-lite` vs `gemini-3.1-flash-lite`** only (both already registered on
  `openrouter-google` with rate cards, `RuEvalGraph.cs:93-98`) — **zero new providers**. DeepSeek/Qwen are
  *not* registered in this commit.
- **5 real fixtures** (the committed Gold events, `KnownBad` anchors excluded from the comparison).
- The `[Fact]` is **advisory** — it never fails on ranking; it prints a ranked per-model scorecard (quality
  composite + per-dim + `cost_usd` + latency + format-reject rate) and passes.
- **The routing switch is a separate owner follow-up** — this commit leaves `appsettings.json` model defaults
  **unchanged**.
- Reuses `SimCoach.RuEval` (`CandidateSource`, `RuJudge`, `ScoreAggregator`, `FixtureLoader`, `EnvGate`).

- New files (all `tests/SimCoach.RuEval/`, one public type each): `AbHarnessOptions.cs` (Tier-2 dev,
  candidate list + `SampleCount` + `EnsureValid`), `AbCandidateOutcome.cs`, `AbScorecard.cs` (pure reducer),
  `AbHarnessTests.cs` (env-gated advisory `[Fact]` + always-on hermetic tests). No `src/` runtime or routing
  change.

**Acceptance (pinned):** `AbScorecard`/`AbHarnessOptions` hermetic tests — **`dotnet test
tests/SimCoach.RuEval` offline (no `SIMCOACH_RU_EVAL`) stays fully green** (hermetic tests pass, network
`[Fact]` returns early); `AbScorecard` ranks a hand-built verdict+usage set correctly (composite, cost
tiebreak) and counts format rejects without corrupting the average; `AbHarnessOptions.EnsureValid` rejects
empty/duplicate candidate lists and `SampleCount < 1`; every declared candidate route resolves to a distinct
`model_id`. Under `SIMCOACH_RU_EVAL=1` + `OPENROUTER_API_KEY` the `[Fact]` emits a non-empty ranked
scorecard and passes without blocking.

### 8. M31 — Bounded confidence enum + logging (observe-only)

*`feat(coach): add bounded confidence enum to real-time tips + logging (M31)`*
Source `[LLM n12/n33]`. **Builds on the shipped M7 abstain gate.**

Give the real-time LLM tip a first-class, bounded self-report (`high`/`low`) of how confident the model is in
the `action_id` it selected: added to the corner/sector/lap output schema, validated **tolerantly**
post-parse (never rejects on it), and surfaced in the M23-style accept/fallback log.

**Owner decision — all realtime cadences, observe-only, log-only, missing→high.**
- **Confidence on all realtime cadences** (corner/sector/lap), gated by a Tier-2 dev flag
  `CoachOptions.RequestConfidence`.
- **Observe-only this commit** — confidence changes **nothing** a driver hears, is billed for, or how
  severity is computed; it only gathers calibration data.
- **RU semantics:** `high` = `Gold clearly supports the chosen action`, `low` = `ambiguous`. **Missing /
  unrecognised → `high`** (default band).
- **Log-only** — surfaced in `LogTipOutcome` (M23 line, one extra field); **no DB column**, no migration.

**MUST-FIX — strict-required invariant + calibration blind spot:**
- **Strict invariant:** when `RequestConfidence` is on, the runtime schema's **`required` must equal
  `keys(properties)`** (OpenAI-strict / Anthropic-tool translators + `OutputSchemaTests` enforce it). So
  `confidence` is added to *both* `properties` **and** `required` in the same edit; `additionalProperties:false`
  stays. A 2-member string `enum` is already proven safe across families (incl. Gemini `responseSchema`, as on
  `action_id`) — verify in `SchemaTranslatorTests`, don't assume.
- **Offline calibration blind spot (documented):** `FakeProvider` (`FakeProvider.cs:29-36`) and template tips
  never emit `confidence`, so under replay/CI every tip **defaults to `high`**. The gathered calibration data
  is therefore only meaningful for **live LLM** runs; offline/replay confidence is a constant and must not be
  read as signal. The tolerant-parse default (missing→high) is what keeps CI green.

- Touches: `OutputSchema.cs:34-60` (add `confidence` enum property + to `required` under `RequestConfidence`),
  `TipValidator.cs:21-79` (tolerant parse, new `out`, never `Reject`), `CoachService.cs:267-320` (thread
  through accept path + `LogTipOutcome`), new `CoachConfidence` enum (own file, `High`/`Low`),
  `CoachOptions` (Tier-2 `RequestConfidence`), `PromptBuilder.cs:60` + `coach.system.v1.ru.txt` (RU high/low
  guidance, appended only when requested). **No gating** — `RuleEngine.ShouldSpeak`, governor, budget, emit/
  abstain untouched.
- **Realtime-path overlap with M32:** both thread a new value through the accept path. **M31 before M32** so
  the confidence `out`/threading is settled before M32 adds corner-identity threading.

**Acceptance (pinned):** `OutputSchemaTests` — **with `RequestConfidence` on, `confidence` is present with
enum `["high","low"]` and `required == keys(properties)` holds; with it off, `required` is back to
`action_id,phrase_ru` and the invariant still holds.** Plus `TipValidatorTests`: accepts `high`/`low`;
**missing → Accept with the `high` default** (FakeProvider parity); unrecognised → default (not Reject);
confidence never changes Accept/Reject/Abstain or the parsed fields. `CoachServiceTests`: the LLM-accept path
logs the parsed confidence; abstain/template log the default; emit-vs-silent is unchanged.

### 9. M32 — Dedup per corner_id + cross-lap memory

*`feat(coach): dedup repeat corner tips with cross-lap memory (#J)`*
Source `[ПД#J]` / `[LLM n37]`. **Composes on top of the shipped M10 cadence-governor — does not
re-implement its per-lap cap or cooldowns.**

Every `CornerEvent` is stateless (deliberately, for golden-testability), so the coach re-says the identical
phrase for the same corner lap after lap (observed: Curva Grande tips 46 & 51, Lesmo 1 tips 48 & 52 —
word-for-word repeats). The M10 governor gates only on **cadence** + **time**, both reset by the next lap.
M32 adds a **semantic** silence gate orthogonal to M10: suppress the *same advice for the same corner*
within a recent-lap horizon (new `QuietReason.RepeatSuppressed`), plus a within-lap idempotency clause.

**Owner decision — `RepeatSuppressionLaps = 2` (Tier-1), High bypasses dedup, exact action_id, `TipIdentity`.**
- **`CadenceOptions.RepeatSuppressionLaps = 2`** (`0` = off) — **Tier-1 user-facing** (XML-doc + `EnsureValid`,
  it shapes chattiness) + **always-on within-lap idempotency** (a given `(corner_id, action_id)` never emits
  twice in one lap, independent of the horizon knob).
- **High-severity BYPASSES dedup** — an explicit `!highSeverity` conjunct in the suppression check, so a
  persistent High fault keeps being flagged (matches M10's one-policy-two-enforcement-points design).
- **Key on exact `action_id` now** (align to M21's action-family key later, as a fast-follow — noted in both).
- **Corner identity via an `in TipIdentity` struct** passed to `ShouldSpeak`/`NoteTip` (future-proofs the
  signature vs a bare `string? cornerId`).
- **Suppression-only** — cross-lap trend/delta injection into the prompt (n37) is **deferred to P3** (it
  enriches *what the LLM says* and risks Gold/prompt surfaces).

**MUST-FIX — hybrid pre/post-LLM gate keyed on lead-vs-spoken coherence:**
- **Pre-LLM gate** suppresses on the **lead action** (`subset[0].Id`) when it matches a recent prior visit's
  recorded action within the horizon — this saves the LLM call when the obvious repeat is about to fire.
- **Post-emit record** stores the **ACTUAL `tip.ActionId`** the driver heard (`NoteTip(cadence, now,
  tip.CornerId, tip.ActionId)`, `CoachService.cs:252`), not the lead — because `BuildChosenTip` may select a
  non-lead subset member. This keeps memory honest: the gate reads the lead pre-LLM, but the memory always
  reflects the spoken action, so lead-vs-spoken never desyncs.

- Touches: `RuleEngine.cs:36-38` (`ShouldSpeak` gains `in TipIdentity`), `:23-25,144-160` (cross-lap
  `Dictionary<string, LastCornerTip>` keyed by corner_id + monotonic `_lapOrdinal` bumped in `ResetLap`;
  memory cleared **only** in `ResetSession`, **not** `ResetLap`), `:144-149` (`NoteTip` records actual
  action), `RuleDecision.cs` (new `QuietReason.RepeatSuppressed`), `CoachService.cs:213-216,252` (pass the
  key), `CadenceOptions.cs` (Tier-1 `RepeatSuppressionLaps`). `GoldArtifactBuilder` stays **stateless** (n37
  forbids making it stateful).
- **No corner_id** (sector/lap cadence, blank id) ⇒ dedup **fails open** (never suppresses), same discipline
  as the frame gates. A suppressed repeat **arms no cooldown** (never reaches `NoteTip`).

**Acceptance (pinned):** `RuleEngineTests` — **a repeated `(cornerA, actionX)` on the next visit is
suppressed (`QuietReason.RepeatSuppressed`) while the horizon has not elapsed, while a High-severity repeat
still Speaks** (the `!highSeverity` bypass). Plus: different action / different corner still Speak; the tip
speaks again after `RepeatSuppressionLaps` `ResetLap` calls; within-lap idempotency holds; no-corner_id fails
open; `ResetSession` clears memory but `ResetLap` does not; a `RepeatSuppressed` silence does not disturb M10
counters. `CoachServiceTests`: two identical corner events on consecutive laps ⇒ `EmitTipAsync` called once;
`NoteTip` records `tip.ActionId`; `EnsureValid` rejects a negative horizon.

---

## Sequencing & dependencies

**Two shared-file clusters force ordering:**

1. **`actionRegistry.json` — M26 → M19 → M21** (wave A, strictly sequential). M26 recalibrates the four
   balance thresholds (ratified before its commit); M19 appends cold-start actions onto the reconciled set;
   M21 rewrites `corner_catch_all` and adds the `ValidSubset` dedup against the full action set.
2. **`CoachService` + `OutputSchema` real-time accept path — M31 → M32** (wave B, sequential). M31 settles
   the confidence `out`-threading; M32 then adds the `in TipIdentity` corner-identity threading in one pass.

**Debrief-path softer ordering — M28 → M20** (informational, cross-wave). M28 (wave B) widens
`TryAcceptDebrief`; M20 (wave A) enriches the debrief output. Under M20's **template-only** default, M20 does
**not** modify `TryAcceptDebrief`, so this is a soft ordering, not a hard blocker (see Open questions).

**Dependencies on already-shipped work:**
- **M31 builds on M7** (abstain) — confidence is only meaningful with the abstain gate; M7 is DONE.
- **M32 composes with M10** (cadence-governor) — additive; must not re-implement M10's cap/cooldowns.
- **M21 interacts with M7** — the dedup must not disable abstain on a genuine catch-all-only corner.
- **M30 reuses the M18** RuEval harness (fixtures, judge, rubric).

**Cross-item couplings to keep converging:**
- **M21 (within-event family) ↔ M32 (cross-lap key).** M21 introduces "same family"; M32 keys on exact
  `action_id` now and aligns to M21's family key as a fast-follow — noted in both so the two dedup layers converge.
- **M26 (normalised balance) ↔ M19/M21 (registry).** M26's recalibrated thresholds are what M19/M21 build on.
- **M28 (Gemini-debrief guard) ↔ M22 (routing).** Both touch routing config; the guard protects the invariant
  M22's fallback route must also honour (never point debrief at a `maxItems`-stripping model — note
  `anthropic/claude-haiku-4.5` is Anthropic-family, so the fallback route is safe by construction).

**Fully independent:** M30 (test project only), M22 (LLM + appsettings, no coach realtime).

---

## Open questions (not silently decided)

1. **M28 → M20 cross-wave ordering.** M20 lands in wave A (PR 1) but its "settle the debrief-validation
   signature first" note points at M28 in wave B (PR 2). Under M20's **template-only** default M20 does not
   touch `TryAcceptDebrief`, so there is no code conflict — but if a later change makes M20 read the validator
   diagnostic, the waves would need reordering or M28 pulled into wave A. Flagged rather than reordered,
   because the owner-ratified template-only default removes the actual coupling. **Recommend:** ship as
   sequenced (A then B); revisit only if M20's scope grows.
