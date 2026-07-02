# Phase-3 P1 — Coaching-quality pack (proto-free, pre-TTS)

## Scope

This pack closes the **P1 coaching-quality** items of the Phase-3 backlog
(`docs/05-implementation/phase-3-master-backlog.md`, P1 table). The detection-truthfulness pack
(PR #26) already lifted the acceptance NO-GO; this pack raises *what the coach says and when*, not
*whether it detects correctly*.

Hard constraints for the whole pack:

- **Proto-free.** No `.proto`, no `Contracts` assembly, no generated-type edits. Every change lives in
  `SimCoach.Coach` / `SimCoach.Pipeline` / `SimCoach.Reference` C# records + runtime JSON schema +
  `IOptions<T>` config, or in a new `tests/` project. Each task section re-confirms this.
- **Each task = exactly one commit** with the conventional subject given in its section.
- **Stacked branch.** Stacked on `feat/phase-3-p1-quality` (off the detection tip): `M9 → M7 → M10 →
  M18`, each commit building on the previous (see *Sequencing & dependencies*).
- **Configurability.** Every threshold is `IOptions<T>` + `EnsureValid`, split into a user-preference
  tier and an internal/advanced tier — see *Принцип конфигурируемости (owner directive)*.
- **Conventions enforced** (`TreatWarningsAsErrors` ON): records/init-only; `_camelCase` incl.
  `private static readonly`; `var` per IDE0007/0008; no magic numbers (`IOptions<T>` + `EnsureValid`);
  `System.Text.Json`; one public type per file; `System.Threading.Channels` for pub/sub; Russian only
  in prompts/fixtures/.resx (English identifiers/comments).

**Out of scope:** M31 (confidence gating) is *not* in this pack. M32 (per-corner + cross-lap dedup +
cross-event ranking) is P2 and deliberately left as a slot M10 sits under without rework. M30 (A/B model
bake-off) is distinct from M18's pass/fail barrier.

**Owner decisions recorded.** Every escalated question was resolved by the owner on 2026-07-02 — see
*Решения владельца (2026-07-02)* at the top. Each task section references the decisions it depends on.
The original *Open decisions* section is retained at the bottom for its rationale, marked resolved.
Agents do **not** re-litigate these.

---

## Решения владельца (2026-07-02)

All escalated questions from the *Open decisions* section below were resolved by the owner on
2026-07-02. They are recorded here **verbatim as binding**; each task section references the ones it
depends on. Agents do **not** re-litigate these.

### Never-silent guarantee (cross-cutting, M7 ∧ M10)

**High severity bypasses ALL three silence sources** — the M10 materiality floor, the global cooldown,
and the per-lap cap. Enforced by an explicit `SeverityFor(lead) != High` guard (defense-in-depth, not an
emergent side effect of current rank values) plus a test that a High tip still **Speaks** after
`MaxTipsPerLap` is reached **AND** within `GlobalCooldown`. One policy, two enforcement points (M7's
abstain guard + M10's `!highSeverity` gates).

### M7 — Abstain (scope = CORNER-ONLY)

- **Scope:** only the weak `corner_catch_all` may abstain (sentinel `action_id="none"`). **Sector/lap
  never abstain** — a silenced lap/sector summary is more jarring than a corner tip.
- **`none` + phrase:** silence — ignore the phrase (the selection to abstain is the signal; the phrase is
  noise).
- **Over-silence guardrail:** log-only (a `LogTipOutcome`-style structured abstain line, **no DB**). Any
  consecutive-abstain forcing is deferred to M10 where cadence governance lives.
- **Cooldown arming:** an abstain does **NOT** arm the cooldown (silence must not consume the speaking
  budget); M10 owns any refinement.
- **Weak-catch-all identification:** via `CoachOptions.CatchAllRank = 900` (config default, proto-free,
  **no registry-schema change**), not a derived threshold or a new `is_catch_all` registry flag.
- **High-severity boundary:** explicit `SeverityFor(lead) != High` guard.

### M9 — Phase-context for `straighter_braking`

- **Overlap window:** turn-in start → apex.
- **Apex fraction:** the **single shared** `ApexWindowFraction` — one definition for both the live gate
  (`CornerPhaseResolver`) and the metric, via a shared pure helper lifted to `SimCoach.Pipeline`, fed to
  the Reference builder **at the App composition edge** — **no second knob**.
- **Threshold:** recalibrate `brake_overlap_steer_pct` from the fixture distribution **AND** pin a
  boundary-fixture regression test.
- **Chicanes:** phase-scoping only (no baked per-corner exempt flag this pack).

### M10 — Cadence-governor

- **Defaults (all tunable):** `GlobalCooldown ≈ 3s`; `MaxTipsPerLap ≈ 5`; per-cadence cooldowns Corner
  4s / Sector 8s (existing); `MinTimeLossMs` materiality floor ≈ 100ms (conservative).
- **Never-silent:** High bypasses all three silence sources (see above).
- **Priority realization:** floor + cap + cooldowns for P1; cross-event/cross-lap **ranking deferred to
  M32**.

### M18 — RU-eval gate

- **Judge:** `anthropic/claude-sonnet-4.6`.
- **Reference-anchored:** a committed canonical RU phrase per fixture (the judge compares candidate to
  reference, not blind).
- **Rubric:** groundedness + brevity/one-imperative + natural Russian + actionability + tone, with a
  **HARD groundedness floor** (a fluent-but-ungrounded phrase can never pass).
- **Enforcement:** hard-fail if a **known-bad anchor** scores *above* the bar (proves the scale broke);
  the good-fixture composite becomes release-blocking only **after calibration**.
- **Run mode:** env-gated like the ground-truth gate — CI stays offline for the network path; the
  **always-on hermetic self-tests must not need the provider chain**.

---

## Принцип конфигурируемости (owner directive)

Maximize user-tunable configuration. Every knob is `IOptions<T>` + `EnsureValid` (no magic numbers), but
splits into **two tiers**. When a knob's tier is ambiguous, **default it to user-facing** and add a note.

### Tier 1 — User preferences (UI-ready)

Knobs a driver would legitimately tune. Group them in a coherent, well-named `IOptions` surface — a new
`CadenceOptions` (a.k.a. `CoachPreferenceOptions`) section under `Coach:Cadence` — each knob carrying an
XML-doc that states its **user-facing meaning + valid range**, `EnsureValid`-guarded, structured so it
can later back a settings-panel of sliders ("ползунки"). Every P1 task that introduces a preference knob
MUST place it here:

- **M10 cadence (this pack):** `GlobalCooldown` (min silence between any two tips), `MaxTipsPerLap`
  (chattiness cap), per-cadence cooldowns (Corner/Sector), `MinTimeLossMs` (materiality floor — "don't
  bother me under N ms").
- **M7 abstain (this pack):** abstain on/off + scope (corner-only by default).
- **Verbosity/chattiness** (future slider surface) belongs here when added.

### Tier 2 — Internal / advanced (dev-config, NOT user sliders)

Detection-correctness and eval knobs a user detuning would produce **wrong coaching**. `IOptions` per
convention, but **not** surfaced as user sliders:

- **M9:** `ApexWindowFraction` (apex band; also drives the metric), plausibility ceilings, kernel
  constants (`BrakeOverlapSteerKernels` 0.10/0.10).
- **M18:** judge model, rubric weights, pass bar, per-dimension floors.

`CatchAllRank` (M7) is a detection/registry heuristic → Tier 2. The M7 abstain **on/off + scope** toggle
is a preference → Tier 1.

---

## M9 — Phase-context for `straighter_braking`

One commit. Proto-free. Stops "Не тормози, выпрямляй руль" from firing in chicanes and straight-line
braking zones by scoping its overlap metric to the turn-in/apex phase.

**Решения:** M9 overlap window, apex fraction (single shared knob), threshold recalibration, and
chicane scope are **resolved** — see [Решения владельца](#решения-владельца-2026-07-02).

### Problem (grounded)

`straighter_braking` (`actionRegistry.json:55-70`) fires when `brake_overlap_steer_pct > 0.3`
(`:63-65`) and phrases `"Не тормози в {corner}, выпрямляй руль."` (`:69`). The metric is
`BrakeOverlapSteerKernels.OverlapPct` (`BrakeOverlapSteerKernels.cs:20-38`): the fraction of frames in
the corner window carrying both `BrakePct > 0.10` (`:15`) and `|SteerRad| > 0.10` (`:18`), computed in
`CornerEventBuilder.cs:60` over `selfSpan` — the **entire** geometric `[StartPosition, EndPosition]`
window (`:38,44`).

Root cause (acceptance §3.2 #F, `phase-3-acceptance.md:84,92`): the check is **phase-blind** and uses
a flat 0.3 threshold over the whole corner. Braking-while-steering is *correct* in a braking chicane
(Variante del Rettifilo) and in legitimate trail-braking, so the whole-window fraction is naturally
high and the tip mis-fires. Observed 5× live: "Не тормози в Variante del Rettifilo (1)".

### Goal

`brake_overlap_steer_pct` measures unwanted overlap **only** in the turn-in/apex portion (where the
brake should already be trailing toward release), not over the pre-corner straight-line approach nor
over a full braking chicane. The `BrakeOverlapSteerPct` field (proto field 14) is unchanged; only the
*value written into it* changes, plus possibly the registry threshold.

### Architecture constraints

- Corner geometry is baked and always available: `Corner` has `StartPosition`/`ApexPosition`/
  `EndPosition` (`TrackModel.cs:19-26`) passed into `CornerEventBuilder.Build` (`CornerEventBuilder.cs:28`).
  No reference lap needed — matches `requires_reference:false`.
- The phase-band arithmetic already exists once, in `CornerPhaseResolver.Resolve`
  (`CornerPhaseResolver.cs:22-59`): `Braking → Entry(turn-in) → Apex → Exit`, wrap-safe via `Mod1`
  (`:62`). But it lives in **`SimCoach.Coach`**, and the dependency direction is
  `Coach → Reference → Pipeline`. The builder (`Reference`) and kernel (`Pipeline`) **cannot** reference
  the resolver — the pure band math must be lifted to a low layer both sides can see.
- The resolver is consumed today only by the live gate (`LiveCoachAmbientState.cs:110` →
  `GateSnapshot.CornerPhase` → `RuleEngine.cs:70` apex-silence). That path is orthogonal: corner-cadence
  actions are evaluated at corner end, when the car is elsewhere, so live-gating cannot fix a
  mis-computed corner metric. **Live-gating `straighter_braking` is considered and rejected** — the
  defect is the metric, not the emit moment.

### Files

- `src/SimCoach.Coach/Data/actionRegistry.json:55-70` — `straighter_braking`; clause `0.3` (`:64`) may
  be recalibrated (M9-threshold).
- `src/SimCoach.Pipeline/Kernels/BrakeOverlapSteerKernels.cs:20-38` — add a phase-scoped entry point
  (overload taking a normalized `[lo,hi]` band, or accept pre-sliced frames). Thresholds `0.10`/`0.10`
  (`:15,:18`) stay as-is.
- `src/SimCoach.Reference/CornerEventBuilder.cs:60` — call site; slice `selfSpan` to the band before the
  overlap call. Reuse/extend `FramesInSpan` (`:149-163`).
- `src/SimCoach.Reference/ComputeOptions.cs` — receives the **shared** apex fraction (not a second knob;
  see must-fix d). `EnsureValid` range mirrors the `(0, 0.5]` style at `RuleEngineOptions.cs:82-85`; if a
  distinct field is unavoidable it carries an `EnsureValid` coupling check against `ApexWindowFraction`.
- `src/SimCoach.Coach/Rules/CornerPhaseResolver.cs:22-62` + `RuleEngineOptions.cs:40` — the resolver is
  refactored to call the lifted pure helper (shared route, per the M9 decision), so the live gate and the
  metric share one definition of "apex".
- `src/SimCoach.App/**` (composition edge) — binds the single `ApexWindowFraction` value once and feeds
  it to **both** `RuleEngineOptions` (Coach live gate) and the Reference builder / `ComputeOptions`.
- Unchanged (proto-free confirmation): `GoldCornerEvent.cs:27`, `GoldArtifactBuilder.cs:42`,
  `CornerGoldView.cs:36`, `GoldFieldNames.cs:19`.

### Approach (proto-free)

1. **Lift the phase-band geometry** to a pure static function in `SimCoach.Pipeline` (same assembly as
   the kernel) taking raw floats `(start, apex, end, apexBandFraction)` and returning the normalized
   turn-in→apex band `[lo, hi]` (and/or a `position → phase` classifier). Floats, not the `Corner`
   record (`Corner` lives in `Reference`). Wrap handled with the same `Mod1` fold as `CornerPhaseResolver.cs:62`.
   This is the **one code definition** of "apex" — both the live gate and the metric call it (so there is
   no *code drift*; the only residual risk is *config drift*, guarded below).
2. **Scope the metric.** In `CornerEventBuilder.cs:60`, compute the band from `corner` (Start/Apex/End) +
   the shared apex fraction, slice `selfSpan`, pass the sliced frames to `OverlapPct`. Empty band ⇒ 0
   (existing kernel contract, `BrakeOverlapSteerKernels.cs:23-26`).
3. **One shared knob — `ApexWindowFraction` (must-fix d).** Do **not** add a second
   `ComputeOptions.BrakeOverlapPhaseBand` knob. Feed the single existing `ApexWindowFraction`
   (`RuleEngineOptions.cs:40`, Tier-2 internal) down to the Reference builder **at the App composition
   edge**: the App binds the value once and injects it into both the Coach live gate and
   `ComputeService`/`ComputeOptions`. If a separate `ComputeOptions` field proves unavoidable (binding
   mechanics), add an `EnsureValid` **coupling check** asserting it equals `ApexWindowFraction` so the two
   can never diverge. **Config-drift ≠ code-drift:** the band arithmetic is one lifted helper (no logic
   duplication); the coupling check only guards against two config values being set differently.
4. **Recalibrate the threshold (must-fix — M9 decision).** The narrower window shifts the fraction's
   distribution, so recalibrate the registry `0.3` (`actionRegistry.json:64`) from the fixture
   distribution **and pin a boundary-fixture regression test** at the chosen value.
5. **Collapse duplication** by refactoring `CornerPhaseResolver` to call the lifted helper (shared route,
   per the M9 decision) — covered by parity tests.

### Risks

- **Threshold drift** — narrowing the window shifts the 0.3 operating point; mitigate with boundary
  fixtures and M9-threshold.
- **Two divergent phase definitions** — if the band math is reimplemented instead of lifted, the live
  gate and the metric can disagree about "apex"; mitigate via M9-shared-helper.
- **Wrap-around corners** — `FramesInSpan` (`:149-163`) uses raw `pos >= start && pos <= end` and does
  not fold across S/F, unlike the resolver's `Mod1`. The band is a sub-range of `[Start,End]` so no
  *new* wrap surface is added, but tests must assert a non-wrapping corner and document the gap.
- **Chicanes may still over-fire** — a braking chicane's turn-in *is* brake+steer. Truly silencing
  chicanes needs a per-corner exempt flag, a baked-asset change beyond a pure phase window
  (M9-chicane-exempt).
- **Existing-test semantics** — `Phase3KernelsTests.cs:77` asserts the whole-window `0.5`;
  `ComputeSessionTests.cs:48` / `CornerEventBuilderTests.cs:41` assert `>0`. The whole-window overload
  must stay (kept as the pure primitive) so those pass.

### Tests

- **Pipeline (`Phase3KernelsTests`)** — keep the whole-window case (`:77`); add overlap frames *outside*
  the band ⇒ phase-scoped result ~0 while whole-window stays high; empty band ⇒ 0.
- **Pipeline (band helper)** — pure tests: `[lo,hi]` for a canonical Start/Apex/End; parity with
  `CornerPhaseResolver` if shared; no-wrap corner.
- **Reference (`CornerEventBuilderTests`)** — synthetic chicane spanning the whole window ⇒
  `BrakeOverlapSteerPct` below threshold (previously above); genuine sustained-brake-into-apex ⇒ still
  above; `requires_reference:false` path still computes.
- **Coach (`ActionRegistryFilterTests`)** — phase-scoped low value ⇒ `straighter_braking` filtered; at/
  above (possibly recalibrated) threshold ⇒ survives. Named `Variante del Rettifilo` false-positive
  guard fixture.
- **Config** — `EnsureValid` rejects an out-of-range apex fraction; if a distinct `ComputeOptions` field
  exists, the coupling check rejects a value that differs from `ApexWindowFraction`.

### Acceptance criteria

- A braking-chicane corner no longer trips `brake_overlap_steer_pct` above the threshold; the Variante
  del Rettifilo fixture stays silent for this action.
- A genuinely late/sustained brake held through turn-in into apex still trips it.
- Metric computed only over the turn-in/apex band, config-driven by a validated fraction (no magic
  number); kernel thresholds unchanged.
- No `.proto`/generated-contract edits; `BrakeOverlapSteerPct` field + Gold plumbing unchanged.
- Single definition of the phase band (or an explicit, documented, tested reason if duplicated).
- `dotnet build` + `dotnet format` clean; all four listed test projects green.

### Commit subject

`fix(coach): phase-scope straighter_braking overlap to turn-in/apex (#F)`

---

## M7 — Abstain / right-to-stay-silent

One commit. Proto-free. **Sources:** `[LLM n8/n29]` (`phase-3-llm-strengthening.md §3.1`, lines
108-117, 236-238), `[PD #I]` (`phase-3-acceptance.md:87`), master-backlog line 73.

**Решения:** M7 scope (corner-only), `none`+phrase (silence), over-silence (log-only), cooldown-arming
(not armed), catch-all source (`CatchAllRank=900`), and the High-severity boundary are **resolved** — see
[Решения владельца](#решения-владельца-2026-07-02).

### Goal

Give the real-time coach a first-class right to stay silent. Today `RuleEngine.ShouldSpeak`
(`RuleEngine.cs:27-100`) is the only silence gate and fires **before** the LLM. Once the subset is
non-empty the LLM must return an enum `action_id`, and any miss falls back to the deterministic template
`subset[0]` (`CoachService.cs:270`) — never silence. The observed failure (LLM-strengthening tip 46/54,
PD #I): when the only thing that fired is the weak `corner_catch_all` (`actionRegistry.json:236-252`,
rank 900, "В {corner} отклонение около {loss}"), the coach reads a raw millisecond number aloud. M7
lets the model answer "nothing useful here — stay quiet" via a sentinel `action_id="none"`, **only** on
a weak catch-all lead, and **never** for High-severity actions. Cost/latency: zero — same single LLM
call, one enum member + one post-parse branch.

### Files

- `src/SimCoach.Coach/Schema/OutputSchema.cs:25-46` — `RealTime(subsetIds)` builds `action_id.enum`; add
  an opt-in `"none"` sentinel. `required` stays `[action_id, phrase_ru]` (strict-mode invariant) — the
  sentinel is an enum member, not a new field.
- `src/SimCoach.Coach/PromptBuilder.cs:53-56` — only caller of `OutputSchema.RealTime`; holds
  `validSubset` + `_coachOptions`. Computes `allowAbstain` and threads it into the schema and (gated on
  the same flag) the `valid_actions`/system-prompt guidance so the model is told the option exists only
  when it does.
- `src/SimCoach.Coach/CoachOptions.cs:33,75-121` — add `CatchAllRank` (int, default 900) + `EnsureValid`
  positivity check.
- `src/SimCoach.Coach/CoachService.cs:200-243` (`ProcessRealtimeAsync`) / `245-271`
  (`CompleteRealtimeAsync`) — interpret `action_id="none"` as silence: skip `_sink.EmitTipAsync` (:231),
  skip `_ruleEngine.NoteTip` (:232 — see M7-cooldown-arming), log an abstain line. Honored **only** when
  abstain was offered for this request (a leaked "none" → ordinary invalid answer → template).
- `src/SimCoach.Coach/TipValidator.cs:15-67` (`TryValidateRealtime`) — currently rejects any
  `action_id ∉ subset` (:45-49). Add an abstain-aware path so a sanctioned "none" is reported as
  *abstain*, distinct from *accept* and *reject* (not retried, not templated).
- Prompt resources (`coach.system.*.ru.txt`, few-shot negatives) — add the RU abstain rule
  (LLM-strengthening line 115). Exact wording is driver-audible → M7-none-with-phrase / prompt review.

### Approach (proto-free)

The `action_id` enum lives in a **runtime JSON string** compiled by `OutputSchema.RealTime`
(`OutputSchema.cs:45`), fed to the LLM as `LlmRequest.JsonSchema` — not a protobuf contract. `CoachTip`,
`LlmRequest`, `RuleDecision` are C# records. Zero proto surface.

1. **Schema.** New `bool allowAbstain` param; when true append the sentinel (`const AbstainActionId =
   "none"`) to `enumArray`. `required`/`properties` unchanged. Strict mode makes the wire schema itself
   the primary guard — when `allowAbstain` is false the model *cannot* emit "none".
2. **Gate (`PromptBuilder`).** Three conjuncts:

   ```csharp
   bool allowAbstain =
       cadence == CoachCadence.Corner                                        // corner-only scope (M7 decision)
       && validSubset[0].Priority.Rank >= _coachOptions.CatchAllRank         // weak-catch-all lead (heuristic)
       && _coachOptions.SeverityFor(validSubset[0].Priority) != CoachSeverity.High;  // never-silent guard
   ```

   - **`rank >= CatchAllRank` is a current-registry heuristic** (must-fix g): the subset is
     priority-ordered (`ActionRegistry.ValidSubset → OrderBy(a => a.Priority)`, `ActionRegistry.cs:102`)
     and *in today's registry* every specific action ranks below 900 while the catch-alls are 900/905/910,
     so a rank-≥-900 lead means "only the undiscriminating catch-all fired". This is an assumption about
     the current registry data, not an invariant — document it as such so a future high-rank specific
     action does not silently qualify for abstain.
   - **Explicit `SeverityFor(lead) != High` guard** (must-fix g / never-silent decision) — defense in
     depth so a future registry authoring a high-priority catch-all can never go silent, independent of
     the rank heuristic.
   - **Corner-only** (must-fix g / M7-scope decision) — require `cadence == CoachCadence.Corner`; sector
     and lap catch-alls never abstain even at rank ≥ 900.
   - The same `allowAbstain` flag gates the RU prompt guidance so the model is invited to abstain only
     when the wire schema actually carries `"none"`.
3. **Interpretation (`CoachService` + `TipValidator`).** Three-way outcome — **accept** / **abstain** /
   **reject**. The abstain-aware branch lives in the public seam `TipValidator.TryValidateRealtime`
   (`TipValidator.cs:15`, currently rejects any `action_id ∉ subset` at `:45-49`), which the private
   `CoachService.TryAcceptRealtime` (`CoachService.cs:413`) already wraps. On abstain,
   `ProcessRealtimeAsync` returns without emitting (equivalent to `RuleOutcome.Silent`,
   `RuleEngine.cs:38-41`) and logs a structured abstain line mirroring `LogTipOutcome`
   (`CoachService.cs:276-283`) — **log-only, no DB columns** (M7 over-silence decision). A sanctioned
   `"none"` abstains **even if the model also returned a non-empty `phrase_ru`** — the phrase is ignored
   (M7 `none`+phrase decision). Abstain is **not** retryable (`IsRetryable`, `CoachService.cs:456-461`,
   only re-asks on a quality miss; a sanctioned "none" is a success) and does **not** arm the cooldown
   (`NoteTip` skipped — M7 cooldown-arming decision). Represent the outcome as a small record / nullable
   `CoachTip?`.
4. **Config.** `public int CatchAllRank { get; init; } = 900;` + `EnsureValid` throw on `<= 0`. Default
   matches `corner_catch_all` (`actionRegistry.json:242`).
5. **Debrief untouched.** `ProcessDebriefAsync` (`CoachService.cs:335-345`) is the terminal
   once-per-session summary — no abstain path.

### Risks

- **Over-silence** — mitigate: narrow gate (weak-catch-all lead only) + observability (M7-over-silence);
  rank-900-lead is naturally rare.
- **Leaked "none"** — a model returning "none" when not offered must **not** be silenced → template.
  Explicit test.
- **Severity coupling** — catch-alls are Exit-phase → Low severity (`CoachOptions.SeverityBands:54`), so
  the rank gate already excludes High; the explicit severity guard (M7-high-boundary) is defense-in-depth.
- **M10 overlap** — abstain (post-LLM, per-tip) and the M10 cadence-governor (pre-LLM, rule-side) both
  produce silence; the cooldown-arming touchpoint is M7-cooldown-arming. Kept orthogonal here.
- **Prompt regression** — new RU guidance can perturb outputs, caught by the M18 gate once landed; until
  then few-shot negatives cover it.

### Tests

Under `tests/SimCoach.Coach.Tests` (+ `SimCoach.LLM.Tests` for schema):

1. **`OutputSchema`** — `allowAbstain:true` enum contains `"none"`; `:false` does not; `required ==
   [action_id, phrase_ru]` in both; sentinel appended once.
2. **`PromptBuilder`** — catch-all lead (rank ≥ `CatchAllRank`) ⇒ schema carries "none"; specific lead
   (e.g. `brake_later_by_meters`, rank 10) ⇒ no "none".
3. **`CoachService` abstain path (FakeProvider)** — "none" when offered ⇒ `EmitTipAsync` never called,
   `NoteTip` not called (M7-cooldown-arming), abstain logged.
4. **High-severity never silent** — lead High/Entry ⇒ no "none"; a "none" reply ⇒ invalid → template.
5. **Leaked "none" safety** — sentinel not offered but model returns "none" ⇒ template, not silence.
6. **No-retry** — abstain does not trigger the retry branch (`CompleteRealtimeAsync:260-268`).
7. **`CoachOptions.EnsureValid`** — `CatchAllRank <= 0` throws.

### Acceptance criteria

- Schema carries `"none"` **iff** the request's lead is a weak catch-all (and not High-severity, per
  M7-high-boundary / M7-scope); specific-action and High-severity requests never carry it.
- A sanctioned `action_id="none"` resolves to **silence** (no `CoachTip`, no TTS, equivalent to
  `RuleOutcome.Silent`), observable in logs, neither retried nor templated.
- High-severity (Entry-phase) tips are never silenced by abstain.
- An unsanctioned "none" falls to the template, never to silence.
- Debrief unaffected. Build + format + tests green. No `.proto`/`Contracts` change.

### Commit subject

`feat(coach): abstain sentinel "none" so a weak catch-all tip can stay silent`

---

## M10 — Cadence-governor

One commit. No `.proto`/contract change: all state lives inside `SimCoach.Coach`, driven by
`RuleEngineOptions` (+ a user-facing `CadenceOptions` split, see *Принцип конфигурируемости*).
**Sources:** `[PD #I, #Q, #J]` (`phase-3-acceptance.md:87, 111, 88`; rationale :30, :161). Master-backlog
line 76. Overlaps M6 (one imperative per phrase — prompt), M7 (abstain), M32 (P2 dedup).

**Решения:** M10-floor, M10-global-cooldown, M10-max-tips, M10 priority realization, and the
never-silent guarantee are **resolved** — see [Решения владельца](#решения-владельца-2026-07-02).
Defaults below are the owner-set values; all remain tunable.

### Goal

Turn the thin quiet-zone gate into a real cadence-governor with the three levers market leaders ship
(acceptance :161):

1. **Materiality floor (priority by time-loss)** — only speak when the loss is material. Today nothing
   gates on measured `delta_ms`; a 20 ms corner speaks like a 400 ms corner.
2. **Cooldowns** — per-cadence exists, but no *global* (cross-cadence) floor, so a corner tip and a
   sector tip can land back-to-back.
3. **One-thing-at-a-time (per-lap cap)** — one tip per event already, but no cap *across a lap*, so a
   busy lap fires a wall of tips.

Per **M10 priority realization** (owner): this pack ships the **floor + cap + cooldowns only**;
cross-event/cross-lap *ranking* (speak the biggest loss first, defer the rest) is deferred to **M32**.
The materiality floor is a *relevance gate*, not a re-ordering of the subset — the authored root-cause
order (`CoachPriority`) is untouched.

### Current state (grounded)

- `RuleEngine.ShouldSpeak(IReadOnlyList<CoachAction> subset, CoachCadence cadence, in GateSnapshot frame,
  in BudgetState budget)` (`RuleEngine.cs:27-28`). Only urgency test is
  `BestPriority(subset) > _options.PriorityFloor` (`:92-95`) — authored `CoachPriority` (phase then rank,
  `CoachPriority.cs:10-16`), never measured time-loss. The engine has no `delta_ms` and no severity input.
- Cooldown is per-cadence only: `_lastEmit` keyed by `CoachCadence` (`RuleEngine.cs:16`), checked at
  `InCooldown` (`:135-144`), armed by `NoteTip` (`:107`), config `Cooldowns` Corner=4s/Sector=8s/others=0
  (`RuleEngineOptions.cs:14-22`). `[PD #J]`: cooldown "only by cadence" → verbatim repeat on adjacent
  laps, two tips on one corner.
- One tip per event: `top = subset[0]` (`CoachService.cs:213`) after `ValidSubset` orders by priority +
  caps at `MaxActionsInMenu` (`ActionRegistry.cs:96-104`). No per-lap counter.
- Time-loss reachable proto-free: `IGoldView.TryGetNumber("delta_ms", out …)`; `delta_ms` is valid for
  every real-time cadence (`GoldFieldNames.cs:17,23,29`). `ProcessRealtimeAsync` already holds `view`
  (`CoachService.cs:203`).
- Severity reachable in the caller (not the engine): `CoachService` holds `_coachOptions`
  (`CoachService.cs:204`) and `subset[0].Priority`, so it can call `_coachOptions.SeverityFor(...)`
  (`CoachOptions.cs:62`, returns `CoachSeverity`) *before* calling `ShouldSpeak`. The pure `RuleEngine`
  must **not** take a `CoachOptions`/`SeverityBands` dependency (layering + purity) — the severity verdict
  is precomputed and passed in as a bool.
- Lap boundary observable: `DomainEventKind.Lap` sets `_currentLap` (`CoachService.cs:182-184`);
  singleton `RuleEngine` is reset per session via `ResetSession` (`RuleEngine.cs:110`).

### Files

- `src/SimCoach.Coach/Rules/RuleEngine.cs:27-100` — widen `ShouldSpeak` to the new signature (below);
  add the materiality floor, the global cooldown, the per-lap cap, and the High-severity bypass over all
  three. `NoteTip` (`:107`) also increments the per-lap counter and arms `_lastEmitGlobal`; add a
  `ResetLap()` seam next to `ResetSession()` (`:110`).
- `src/SimCoach.Coach/Rules/RuleEngineOptions.cs:11-91` — internal/advanced cadence knobs live under a new
  user-facing `CadenceOptions` (see *Принцип конфигурируемости*); the raw `MinTimeLossMs`,
  `GlobalCooldown`, `MaxTipsPerLap` values are bound there and threaded into the engine. `EnsureValid`
  guards mirror the existing throw style (`:61-91`).
- `src/SimCoach.Coach/Rules/RuleDecision.cs:17-32` — new `QuietReason` members (`BelowTimeLossFloor`,
  `GlobalCooldown`, `LapTipBudget`).
- `src/SimCoach.Coach/CoachService.cs:200-243` — **precompute both new args** and pass them positionally:
  `double timeLossMs = view.TryGetNumber("delta_ms", out double d) ? Math.Abs(d) : 0;` and
  `bool highSeverity = _coachOptions.SeverityFor(subset[0].Priority) == CoachSeverity.High;`. Call
  `ResetLap()` on the `Lap` event (`:182-187`). The M23 accept/fallback log (`:276-283`) + silent debug
  line (`:209`) already surface `decision.Reason` — no new logging plumbing.
- `src/SimCoach.App/**` + `appsettings.json` — bind the cadence options under `Coach:Rules` /
  `Coach:Cadence` (`RuleEngineOptions.cs:8`).

### New `ShouldSpeak` signature (must-fix a)

```csharp
public RuleDecision ShouldSpeak(
    IReadOnlyList<CoachAction> subset, CoachCadence cadence, in GateSnapshot frame, in BudgetState budget,
    double timeLossMs, bool highSeverity)
```

`timeLossMs` and `highSeverity` are **precomputed positional args** supplied by `CoachService`
(`CoachService.cs:205`), which already holds `_coachOptions`. The pure `RuleEngine` gains **no**
`CoachOptions`/`SeverityBands` dependency — it only reads the two scalars. This ripples to the ~20
existing `ShouldSpeak` call-sites in `RuleEngineTests` (every call must append the two args); the
mechanical fix is `timeLossMs:` above the floor and `highSeverity: false` for cases that are not
exercising the new gates, plus the new dedicated cases below.

### Approach (proto-free)

1. **Materiality floor.** After the frame/quiet-zone hard silences and before the priority-floor check:
   `if (!highSeverity && timeLossMs < _options.MinTimeLossMs) return Silent(BelowTimeLossFloor);`.
   `timeLossMs` is already `Math.Abs`-ed by the caller (`delta_ms` may be signed self−ref); 0 = "no loss
   known" so the floor fails **open** like the frame gates. Because `delta_ms` is per-event (one value)
   the subset is not reordered — the loss is a **relevance floor** ("ранжирует по влиянию на время
   круга… остальное подождёт", :161), consistent with M10 priority realization (ranking → M32).
2. **Global cross-cadence cooldown.** Add a single `_lastEmitGlobal` timestamp; `NoteTip` updates it
   alongside `_lastEmit[cadence]`; guard `if (!highSeverity && _clock.GetUtcNow() - _lastEmitGlobal <
   _options.GlobalCooldown) return Silent(GlobalCooldown);`. Per-cadence cooldowns (Corner 4s / Sector 8s)
   remain the finer lever on top.
3. **One-at-a-time across a lap.** Add `_tipsThisLap` + `_options.MaxTipsPerLap`;
   `if (!highSeverity && _tipsThisLap >= _options.MaxTipsPerLap) return Silent(LapTipBudget);`. `NoteTip`
   increments `_tipsThisLap`; `ResetLap()` zeroes it (on the Lap event); `ResetSession` also resets it +
   `_lastEmitGlobal`.
4. **Never-silent (must-fix c).** All three new gates carry the **explicit `!highSeverity` guard** — a
   High-severity lead bypasses the materiality floor, the global cooldown, **and** the per-lap cap. This
   is the same never-silent guarantee as M7's `SeverityFor(lead) != High` (one policy, two enforcement
   points). Defense-in-depth: even a future registry that authored a high-priority catch-all can never be
   silenced by cadence governance.
5. **Config + fail-fast.** `MinTimeLossMs` (double, ≥ 0), `GlobalCooldown` (TimeSpan, ≥ 0 = off),
   `MaxTipsPerLap` (int, > 0), all in `EnsureValid` with the existing throw style. Owner defaults:
   `MinTimeLossMs ≈ 100` (conservative floor), `GlobalCooldown ≈ 3s`, `MaxTipsPerLap ≈ 5`.
6. **Gate ordering (load-bearing).** The three new gates go *after* the session/contact/off-track/
   user-zone hard silences (`RuleEngine.cs:44-85`) and interleave with the existing cooldown +
   priority-floor block (`:87-95`), so a suppressed tip never reaches `NoteTip` and does not consume the
   per-lap budget. Budget downgrade (TemplateOnly) stays last (`:97-99`).

### Composition with M6 / M7 (explicit)

- **M7 (abstain):** M7 lets the LLM answer "none" on the weak catch-all → silence *downstream* of the
  gate. M10's floor is *upstream* (the tip never reaches the LLM). Complementary: floor kills
  trivial-loss corners cheaply; abstain kills "nothing useful to say" on material-loss corners. A corner
  both below-floor and would-abstain is fine (both mean quiet). Per the owner decision, an **abstain does
  NOT arm the cooldown** (silence should not consume the speaking budget) — M10 owns that touchpoint.
  The High-severity bypass here agrees with M7's `SeverityFor != High` guard — one ratified policy.
- **M6 (one imperative per phrase — prompt):** M10 governs *whether/when*; M6 governs *what one thing*.
  No code overlap; the per-lap cap is the code-side complement to M6's phrase rule.
- **M32 (P2):** per-`corner_id`+lap dedup + cross-lap memory + cross-event ranking. M10 ships only
  floor + lap-count + global cooldown so M32 slots on top without rework.

### Risks

- **Signature ripple** — the two new positional args touch every `RuleEngineTests` call-site (~20);
  mechanical, listed above. Kept plain scalars (no options injected into the pure engine).
- **Over-silencing** — floor + global cooldown + lap cap compound; mitigated with the conservative owner
  defaults, the High-severity bypass, and the M23 silence-reason log (`:209, 276-283`).
- **Lap-reset timing** — the Lap event arrives *after* that lap's corners; the counter governs "tips
  since the last lap boundary", the intended lap-scoped budget — document so it is not read as a strict
  per-lap-N cap.
- **delta_ms sign** — may be signed (self−ref); the caller passes `Math.Abs`. Sign truthfulness (a *gain*
  should not trigger a "you lost time" tip) is M1/M2/M3's job (shipped, PR #26); M10 gates magnitude.
- **No proto drift** — `delta_ms` read through the existing `IGoldView` seam.

### Tests

New `RuleEngineTests` (pure, fake clock already used, `RuleEngine.cs:15,18`; no sleeps):

- Below-floor → `Silent(BelowTimeLossFloor)`; at/above → `Speak`.
- **Never-silent (must-fix c):** a High-severity tip (`highSeverity: true`) still `Speak`s after a full
  `MaxTipsPerLap` worth of `NoteTip`s **AND** within `GlobalCooldown` **AND** below `MinTimeLossMs` — the
  single test that asserts High bypasses all three silence sources at once.
- Global cooldown: two cadences in quick succession → second `Silent(GlobalCooldown)`; after the window
  → `Speak`.
- Per-lap cap: `MaxTipsPerLap` `NoteTip`s then `ShouldSpeak` → `Silent(LapTipBudget)`; `ResetLap()`
  re-opens.
- `ResetSession` clears the lap counter + global timestamp.
- `EnsureValid` throws on negative `MinTimeLossMs`, negative `GlobalCooldown`, non-positive `MaxTipsPerLap`.
- `CoachService` integration: corner Gold with below-floor `delta_ms` emits nothing + logs the reason;
  Lap event resets the counter.

### Acceptance criteria

- `ShouldSpeak` takes precomputed `timeLossMs` + `highSeverity`, and gates on the materiality floor, a
  global cross-cadence cooldown, and a per-lap tip cap — all config-driven, all in `EnsureValid`.
- A High-severity lead bypasses **all three** silence sources (floor, global cooldown, per-lap cap); the
  dedicated never-silent test passes.
- The pure `RuleEngine` gains no `CoachOptions`/`SeverityBands` dependency.
- No `.proto`/generated-contract change; `delta_ms` read via `IGoldView` only.
- Every silence path returns a distinct `QuietReason`, visible in the M23 log.
- Build + format clean; new RuleEngine tests (incl. the ~20 updated call-sites) pass.
- Owner defaults wired: `MinTimeLossMs ≈ 100`, `GlobalCooldown ≈ 3s`, `MaxTipsPerLap ≈ 5` (all tunable).

### Commit subject

`feat(coach): cadence-governor — materiality floor, cooldowns, per-lap cap`

---

## M18 — RU-eval gate (LLM-judge + rubric + fixtures + numeric bar)

One commit. Proto-free (test/eval only; reuses `LlmRequest`/`OutputSchema` — no contract, no
runtime-behavior change). Master-backlog row `phase-3-master-backlog.md:84` (P1). Source `[PD #23]`.
Plan sketch `phase-3-detailed-plan.md:1108-1115, 1132`.

**Решения:** judge (`anthropic/claude-sonnet-4.6`), reference-anchored judging, the 5-dimension rubric
with a hard groundedness floor, and the enforcement stance (hard-fail known-bad anchors; good-fixture bar
release-blocking only post-calibration; env-gated) are **resolved** — see
[Решения владельца](#решения-владельца-2026-07-02).

This is the **regression barrier** for every prompt/gold edit in the pack (M5, M6, M8, M11, M12, M17 all
say "measure via M18"), and the single remaining open item of the minimal GO bar
(`phase-3-master-backlog.md:14, 146`).

### Goal

A held-out RU-quality eval defined as a *real* gate: feed committed Gold fixtures (no-PB / corner /
debrief) through the **existing** prompt+LLM path to produce candidate RU phrases, have an **LLM judge**
score them against a rubric, and pass/fail against a **numeric bar**. It runs **per release, not in the
no-network CI lane** — env-gated exactly like the ground-truth gate, so offline CI stays green and no
API key is needed to build/test. Its job is to let all the prompt edits above be measured instead of
eyeballed, and to *block* the `gemini-2.5-flash-lite` real-time swap and any DeepSeek un-gating until RU
quality is proven (`phase-3-detailed-plan.md:1110-1111, 1132-1133`).

### Files

Reuse (do not modify unless noted):

- `tests/SimCoach.Reference.Tests/GroundTruthRevalidationTests.cs:40, 56-63, 168-181` — the **pattern to
  mirror**: a `[Fact]` gated on an env var; unset → `return;` (clean skip = CI path); fixture/truth from
  a local dir; class-doc documents the run-book. M18 copies this shape (add `SIMCOACH_RU_EVAL`).
- `src/SimCoach.LLM/ILlmClient.cs:8-14` — provider-agnostic seam; `CompleteAsync(LlmRequest, ct)` is the
  one call the eval needs for both candidate generation and the judge.
- `src/SimCoach.LLM/LlmRequest.cs:9-14` — `record(RouteKey, SystemPrompt, UserPrompt, JsonSchema,
  SchemaName)`; the judge call is another `LlmRequest` on a new `"ru_judge"` route key.
- `src/SimCoach.LLM/LlmResult.cs:10-12` — `Success(Json, Usage, Info)` / `Failure(Error)`; verdict parsed
  via `System.Text.Json`.
- `src/SimCoach.LLM/LlmServiceCollectionExtensions.cs:20-68` — `AddLlm` composes the real ring:
  `LlmUsageRepository` (`:40`), `SqliteCostMeter` as `ICostMeter` (`:41`), `SqliteCostQueryRepository` as
  `ICostQueryRepository` (`:42`), the named `HttpClient`s, the per-provider decorator chains, and
  `LlmRouter` as `ILlmClient` (`:63-65`). **`SqliteCostMeter` depends on `ISessionIdProvider`**, which
  `AddLlm` does **not** register — it is bridged at the App edge (`CoachComposition.cs:26` →
  `SessionContextSessionIdProvider`, per the class-doc at `:15-16`). **Eval plumbing (must-fix f):** the
  eval builds the same graph from a committed `appsettings`, flips `Llm:Live=true`, resolves `ILlmClient`,
  and must additionally (a) open a **throwaway SQLite connection** (in-memory or temp file) so
  `SqliteCostMeter`/`ICostQueryRepository`/`LlmUsageRepository` resolve, and (b) **register an
  `ISessionIdProvider`** (a trivial stub returning a fixed id, or reuse the App composition helper). Note:
  the always-on hermetic self-tests do **not** need this network/cost graph — they exercise pure
  aggregator/parser/EnsureValid code with no `ILlmClient`.
- `src/SimCoach.App/appsettings.json:56-64, 75-80` — route table + `openrouter-anthropic` provider
  (`anthropic/claude-sonnet-4.6`, $3/$15). A new `"ru_judge"` route is added to the **eval's**
  appsettings only.
- `src/SimCoach.Coach/PromptBuilder.cs:28, 64` — `Build<TEvent>(gold, cadence, subset) → LlmRequest`; the
  corner/no-PB fixtures drive candidates through this (identical to production).
- `src/SimCoach.Coach/Gold/GoldArtifactBuilder.cs` + `GroundTruthRevalidationTests.cs:186-205` — the
  **exact fixture-build pattern to copy** (must-fix b): `new GoldArtifactBuilder(CornerNameMap.Load(),
  new CoachOptions())` → `BuildCorner(CornerEvent, ctx)` / `BuildSession(SessionEvent, ctx)` →
  `CornerGoldView` / `DebriefTemplate.BuildJson`, with `GoldSessionContext(...HasReference:false)` for the
  no-PB case. Fixtures are committed as **proto-event JSON** (`CornerEvent` / `SessionEvent`) and run
  through this builder — **not** as pre-built Gold JSON (there is **no Gold-JSON deserializer**; nothing
  reads a serialized Gold artifact back in).
- `src/SimCoach.Coach/Schema/OutputSchema.cs:25-46, 53-85` — `RealTime`/`Debrief` produce the candidate
  schema; the judge's tiny verdict schema is a new static in the eval project.
- `src/SimCoach.Coach/TipValidator.cs:15` — the **public** `TryValidateRealtime(json, subsetIds, maxWords,
  …)` seam (word-cap + enum-membership + non-empty) the eval calls directly to separate "well-formed" from
  "quality" outcomes (must-fix e). `CoachService.TryAcceptRealtime` (`CoachService.cs:413`) is **private**
  and just wraps this validator over an `LlmResult`, so the eval reuses the public validator, not the
  private wrapper.

New (this commit):

- `tests/SimCoach.RuEval/` — **new project** (name: `SimCoach.RuEval`). Holds the env-gated `[Fact]`, the
  fixture loader, the candidate-generation harness (Gold → `PromptBuilder`/`DebriefTemplate` →
  `ILlmClient`), the judge wrapper, the rubric options, the score aggregator.
- `tests/SimCoach.RuEval/Fixtures/` — committed **proto-event JSON** (`CornerEvent` / `SessionEvent`) for
  no-PB / corner / debrief, each paired with a **committed canonical RU reference phrase** (reference-
  anchored judging, M18 decision). RU text (reference phrases) lives here; identifiers/comments English.
- `tests/SimCoach.RuEval/Prompts/ru-judge.system.ru.txt` + verdict schema — the judge instruction (RU
  rubric) and its strict JSON output schema.
- `RuEvalOptions.cs` — `IOptions<T>` carrying rubric weights, pass bar, judge route key, sample count,
  with `EnsureValid()` (no magic numbers).
- `SimCoach.sln` + `./scripts/bootstrap.sh` — regenerate sln/restore after adding the project.

### Approach (proto-free)

1. **Fixtures (must-fix b).** Commit a small held-out set of **proto-event JSON** fixtures spanning the
   three families (`phase-3-detailed-plan.md:1108-1110`): a no-PB / cold-start corner
   (`HasReference:false`), a reference-relative corner, a debrief (`SessionEvent`). At load time build the
   Gold artifact through `GoldArtifactBuilder.BuildCorner`/`BuildSession` **exactly like**
   `GroundTruthRevalidationTests:186-205`, then feed it to `PromptBuilder`/`DebriefTemplate`, identical to
   runtime. (No Gold-JSON deserializer exists, so fixtures cannot be pre-built Gold JSON.) Each fixture
   also carries its committed canonical RU reference phrase for the anchored judge.
2. **Candidate generation (production path).** For each fixture build the `LlmRequest` via the real
   `PromptBuilder.Build` (or the debrief path), send through the resolved live `ILlmClient` on the route,
   run the answer through the **public** `TipValidator.TryValidateRealtime` (must-fix e) so malformed
   answers report as *format* failures, not *quality* failures.
3. **LLM judge (reference-anchored).** A new `"ru_judge"` route (eval appsettings only) points at
   `anthropic/claude-sonnet-4.6` (M18-judge decision). The judge receives fixture context (Gold facts +
   coaching intent), the **committed canonical RU reference phrase**, and the candidate `phrase_ru`, and
   returns a strict-schema verdict: per-dimension scores over the 5-dimension rubric — groundedness,
   brevity/one-imperative, natural Russian, actionability, tone (M18-rubric decision) — + a short RU
   justification. Reuse `CompleteAsync` + a tiny strict verdict schema; parse with `System.Text.Json`.
   Optionally average `SampleCount` calls to damp nondeterminism.
4. **Aggregation + bar.** Combine per-dimension scores into a composite via `RuEvalOptions` weights;
   assert the composite clears the bar **and the HARD groundedness floor** (M18-rubric/bar decision) — a
   fluent-but-ungrounded phrase can never pass. Enforcement (M18-gate decision): **hard-fail if a
   known-bad anchor scores above the bar** (proves the scale broke); the good-fixture composite becomes
   release-blocking only **after calibration**. On fail, dump every fixture's candidate + verdict +
   justification via `ITestOutputHelper` (as the ground-truth gate does).
5. **Env-gate + run mode.** The `[Fact]` returns early (skip) unless `SIMCOACH_RU_EVAL` is set **and** an
   API key is present, mirroring `GroundTruthRevalidationTests.cs:59-63`. Default `dotnet test` (offline)
   skips it; a release runner sets the env var + `OPENROUTER_API_KEY`. Whether the result *blocks* a
   release or is advisory-only is M18-gate.

All thresholds/weights are `IOptions<T>` + `EnsureValid`; records/init-only; `System.Text.Json`; one
public type per file; RU only in fixtures/judge prompt.

### Risks

- **Judge nondeterminism** — mitigate: `Temperature=0` on the judge route (coach routes already do,
  appsettings:60-64), optional N-sample averaging, a **margin** in the bar rather than a razor edge;
  reference-anchored judging (M18-reference-judging) stabilizes further.
- **Self-preference / lenient judge** — a same-family judge may over-score; mitigate via M18-judge and a
  couple of committed *known-bad* fixtures (transliterated corner name, raw-number-in-voice — the exact
  M5/M6 failures) that the gate asserts score *below* the bar, anchoring the scale.
- **Flaky/expensive network in the release lane** — bounded fixture count, per-route `Timeout`, out of
  the CI lane by design; cost is a few Sonnet calls per release.
- **Fixture rot** — Gold-JSON decouples from prompt text, but a Gold *schema* change needs a fixture
  refresh; document the regen path in the class-doc.
- **Scope creep into M30** — M18 is a pass/fail barrier, not the A/B bake-off (M30,
  `phase-3-master-backlog.md:105`); single model under test per run.

### Tests

- **Hermetic self-tests (always-on, no network):** aggregator math over synthetic scores → expected
  composite; `RuEvalOptions.EnsureValid` rejects out-of-range weights/bar; verdict JSON parser accepts a
  golden verdict and rejects a malformed one; the env-gate returns-early cleanly when `SIMCOACH_RU_EVAL`
  is unset (proves CI stays offline). Mirror the always-on helper `[Fact]` in
  `GroundTruthRevalidationTests.cs:149-166`.
- **Known-bad anchors (network, gated):** the transliteration + raw-number fixtures score below the bar.
- **The gate itself (network, gated):** the three good fixtures clear the bar; on fail, full dump.

### Acceptance criteria

- New env-gated `tests/SimCoach.RuEval` builds under `TreatWarningsAsErrors`, passes `dotnet format
  --verify-no-changes`, added to `SimCoach.sln` via bootstrap.
- With `SIMCOACH_RU_EVAL` **unset**, `dotnet test SimCoach.sln` runs fully offline and the gate skips —
  no network, no API key.
- With the env var + `OPENROUTER_API_KEY`, the gate generates candidates through the *production*
  `PromptBuilder`/debrief path, judges them, asserts the numeric bar; the two known-bad anchors fail; a
  failing run prints every candidate + verdict.
- Rubric, judge model, bar, and gate-vs-advisory wired from `RuEvalOptions`/eval appsettings — no magic
  numbers — with the **owner-decided** values (M18-rubric/judge/bar/gate) recorded before implementation.
- No `.proto`/contract/runtime change; documented in the class-doc run-book.

### Commit subject

`test(eval): add env-gated RU-eval gate — LLM judge + rubric + fixtures (M18)`

---

## Sequencing & dependencies

Stacked on `feat/phase-3-p1-quality` (off the detection tip), one commit per task, in this order:

1. **M9 (metric fix)** — self-contained; touches `Pipeline`/`Reference`/registry only. No dependency on
   the others. First because it is the lowest-layer change and its shared phase-band helper (if
   M9-shared-helper takes the shared route) is pure infrastructure the rest can ignore.
2. **M7 (abstain)** — post-LLM, `Coach`-only. Independent of M9. Introduces the `CatchAllRank` config and
   the three-way accept/abstain/reject outcome that M10 reasons about.
3. **M10 (cadence-governor)** — pre-LLM, rule-side. Sequenced after M7 so the **High-severity
   never-silent** policy is settled once (M7-high-boundary and M10-floor must be answered consistently)
   and so the M7-cooldown-arming interaction (does an abstain arm the cooldown?) is resolved in M10 where
   cadence governance lives. M10 also owns any consecutive-abstain forcing deferred from M7-over-silence.
4. **M18 (RU-eval gate)** — last, as the regression barrier. It reuses the *shipped* prompt path, so it
   should land after the prompt-affecting edits (M7's RU abstain guidance) to codify their expected
   quality. Once landed it guards all future prompt/gold edits, including the model swaps it is designed
   to block.

Dependency notes:

- **M7 ↔ M10 (High-severity):** one policy, two enforcement points. The floor bypass (M10) and the
  never-abstain guard (M7) must agree: a High-severity tip is neither floored nor abstained. Resolve
  M7-high-boundary and M10-floor together.
- **M7 ↔ M10 (silence composition):** M10's floor is upstream (tip never reaches the LLM); M7's abstain
  is downstream (LLM chose silence). Both legitimately produce quiet; the only shared knob is
  cooldown-arming (M7-cooldown-arming), owned by M10.
- **M6 (out of this pack, prompt-side):** governs *what one thing* a phrase says; M10's per-lap cap is
  its code-side complement. No code overlap.
- **M31 (confidence) — out of scope.** Not addressed here.
- **M32 (P2 dedup) — out of scope.** M10 intentionally ships only lap-count + global cooldown so M32
  (per-corner + cross-lap memory) slots on top without rework.

---

## Open decisions — RESOLVED 2026-07-02 (rationale archive)

> **All resolved.** The owner's binding answers are recorded verbatim in
> [Решения владельца (2026-07-02)](#решения-владельца-2026-07-02) at the top of this doc; the recommended
> option was taken in every case. This section is retained only for the **rationale** (why each was
> escalated + the options considered). Do **not** treat anything here as open.

These were escalated because they set **driver-audible behavior**, **release policy**, or an
**irreversible data/asset shape**. Listed verbatim with options + the recommendation that became the
decision.

### M7 — Abstain

**M7-scope — Which cadences may abstain — corner-only, or all real-time catch-alls (corner+sector+lap)?**
- *Why:* driver-audible product boundary — silencing a lap/sector summary is more jarring than a corner
  tip, and the sources conflict (LLM §3.1 uses a rank ≥ 900 gate spanning all three; the boundary note
  says lap/sector milestones don't abstain). Not an agent call.
- *Options:* (a) Corner-only (extra cadence guard); (b) All three catch-alls via the rank gate
  (corner+sector+lap); (c) Corner+sector, never lap.
- *Recommendation:* **Corner-only** for this pack — targets the exact observed complaint (tip 46
  `corner_catch_all` reading a raw number) and keeps lap/sector summaries always spoken; widen later if
  over-catch-all persists there.

**M7-high-boundary — How hard is the "High-severity never silent" boundary enforced?**
- *Why:* the safety promise that a critical tip is never suppressed — a user-facing guarantee. Must be an
  explicit owner-ratified rule, not an emergent side effect of current rank values. (Answer consistently
  with M10-floor.)
- *Options:* (a) Rely on the catch-all-rank gate only (catch-alls are already Exit/Low); (b) Add an
  explicit `SeverityFor(lead) != High` guard as defense-in-depth; (c) Also forbid abstain for Medium
  severity.
- *Recommendation:* **Add the explicit `SeverityFor != High` guard** (defense-in-depth) so a future
  registry that authors a high-priority catch-all can never accidentally go silent.

**M7-none-with-phrase — If the model returns `action_id="none"` but also a non-empty `phrase_ru`, what
wins?**
- *Why:* defines exactly what the driver does or doesn't hear in a genuine model-behavior edge case; a
  phrase spoken under a "none" selection would be ungrounded output.
- *Options:* (a) Silence — ignore the phrase (selector chose to abstain); (b) Treat as invalid → template
  fallback; (c) Speak the phrase anyway.
- *Recommendation:* **Silence and ignore the phrase** — the selection to abstain is the signal; the
  phrase is noise. Test covers it.

**M7-over-silence — What over-silence guardrail ships with abstain?**
- *Why:* determines whether the coach can go quiet for a whole session unnoticed — a direct
  driver-experience risk the owner should accept explicitly.
- *Options:* (a) Structured abstain-rate log line only (like M23 `LogTipOutcome`), no DB; (b) Log + a
  max-consecutive-abstain safety that forces a template after N; (c) Nothing yet.
- *Recommendation:* **Log-only** for this pack (zero-cost observability, mirrors existing M23 logging);
  defer any consecutive-abstain forcing to M10 where cadence governance lives.

**M7-cooldown-arming — Does an abstain (silence) arm the per-cadence cooldown, and how does that compose
with M10?**
- *Why:* the concrete overlap point with the M10 cadence-governor; changes future speaking cadence — an
  owner decision, not an agent one.
- *Options:* (a) Abstain does NOT arm the cooldown (nothing was said, next corner may speak); (b) Abstain
  arms the cooldown like a spoken tip; (c) Defer entirely to M10 and arm nothing now.
- *Recommendation:* **Do not arm the cooldown on abstain** — silence should not consume the speaking
  budget; flag the interaction so M10 owns any refinement.

**M7-catchall-source — How is "weak catch-all" identified — config rank threshold, derived from the
registry, or an explicit registry flag?**
- *Why:* affects maintainability and whether the registry data schema changes; an explicit flag is more
  robust but touches `actionRegistry.json`/loader, a wider blast radius.
- *Options:* (a) New `CoachOptions.CatchAllRank=900` (config, proto-free, smallest); (b) Derive the
  threshold from the min rank among `*_catch_all` ids at load; (c) Add an explicit `is_catch_all` boolean
  to `actionRegistry.json` + loader.
- *Recommendation:* **`CoachOptions.CatchAllRank=900`** (config default) — smallest, IOptions-compliant,
  no registry-schema churn; revisit the explicit flag if more catch-all-like actions are added.

### M9 — Phase-context for `straighter_braking`

**M9-band — What is the turn-in/apex band definition (and default `ComputeOptions.BrakeOverlapPhaseBand`)
the metric is scoped to?**
- *Why:* driver-audible — the band boundary decides which frames count toward the overlap and therefore
  *when* the "Не тормози, выпрямляй руль" tip fires. Not an agent call.
- *Options:* (a) Turn-in start → apex only (tightest; excludes the braking approach and exit); (b) Turn-in
  start → apex + first half of exit (captures late brake release past apex); (c) Braking-phase end →
  apex (includes the trail-brake handover).
- *Recommendation:* **Turn-in start → apex (a)**, with the apex-band fraction defaulting to the value
  already used by `CornerPhaseResolver`'s `ApexWindowFraction` so the metric and the live gate share one
  definition of "apex".

**M9-threshold — After the window narrows, is the registry `brake_overlap_steer_pct > 0.3` threshold
recalibrated, and to what?**
- *Why:* driver-audible — a narrower measurement window shifts the fraction's distribution, so keeping
  0.3 could make the tip over- or under-fire in the opposite direction.
- *Options:* (a) Keep 0.3 and validate against fixtures; (b) Recalibrate to a new value chosen from the
  fixture distribution; (c) Recalibrate and add a boundary-fixture regression test at the chosen value.
- *Recommendation:* **(c)** — recalibrate from the fixture distribution *and* pin a boundary fixture, so
  the operating point is grounded in data and guarded against future drift.

**M9-chicane-exempt — Rely on phase-scoping alone, or add a baked per-corner "chicane/exempt" flag?**
- *Why:* scope + irreversible asset shape — a per-corner exempt flag is a baked-geometry (landmark) change
  beyond a pure phase window, with a wider blast radius; the owner should choose the ambition level.
- *Options:* (a) Phase-scoping only — accept that a braking chicane's turn-in is genuinely brake+steer and
  may still fire; (b) Phase-scoping + a baked per-corner exempt flag for known chicanes.
- *Recommendation:* **(a) Phase-scoping only** for this pack — it fixes the observed straight-line/whole-
  window mis-fire without a landmark-asset change; escalate a chicane exempt flag as a separate baked-asset
  task if chicanes still over-fire after phase-scoping.

**M9-shared-helper — Collapse the phase-band math into one shared low-layer helper, or allow a second
definition in the kernel/builder?**
- *Why:* engineering + long-term correctness — two definitions of "apex" (live gate vs metric) can drift
  and produce confusing behavior; sharing has a small refactor cost on `CornerPhaseResolver`.
- *Options:* (a) Lift the band math to a shared pure helper in `SimCoach.Pipeline` and refactor
  `CornerPhaseResolver` to call it; (b) Duplicate the band math in the kernel/builder and document the
  divergence.
- *Recommendation:* **(a) Shared helper** — single definition, no drift; the refactor is pure and covered
  by parity tests.

### M10 — Cadence-governor

**M10-floor — What is `MinTimeLossMs` (the time-loss floor), and does High severity bypass it?**
- *Why:* driver-audible — the floor decides which corners are "material enough" to speak about; the
  High-severity bypass is the same never-silent guarantee as M7 and must be ratified consistently.
- *Options:* (a) Conservative floor (small, e.g. ~100 ms) with High-severity bypass; (b) Higher floor
  (e.g. ~200 ms) with High-severity bypass; (c) A floor with **no** bypass (High is also floored).
- *Recommendation:* **(a)** — a conservative floor plus a High-severity bypass, matching M7's "High never
  silent"; tune upward later if the coach is still too talkative. Answer jointly with M7-high-boundary.

**M10-global-cooldown — What is the global cross-cadence `GlobalCooldown`?**
- *Why:* driver-audible cadence — this is the minimum silence between *any* two tips (corner + sector no
  longer stack back-to-back); too long feels unresponsive, too short defeats the purpose.
- *Options:* (a) Short (~3 s); (b) Medium (~5 s); (c) Off (0 = disabled, rely on per-cadence cooldowns
  only).
- *Recommendation:* **(a) ~3 s** — enough to stop back-to-back stacking without muting distinct events;
  the finer per-cadence cooldowns (Corner 4 s / Sector 8 s) still apply on top.

**M10-max-tips — What is `MaxTipsPerLap` (the one-thing-at-a-time per-lap cap)?**
- *Why:* driver-audible cadence — caps how chatty a busy lap can get; too low silences legitimate
  distinct issues, too high re-admits the "wall of tips" complaint.
- *Options:* (a) Tight (~3 per lap); (b) Moderate (~5 per lap); (c) Generous (~8 per lap).
- *Recommendation:* **(b) ~5 per lap** — leaves room for a few genuinely distinct corners while cutting
  the wall-of-tips; combined with the floor + global cooldown it self-limits further.

### M18 — RU-eval gate

**M18-rubric — What quality dimensions does the judge rubric score?**
- *Why:* driver-audible by proxy — the rubric defines what "good RU coaching" means and therefore what all
  future prompt edits are optimized toward; an owner-owned quality definition, not an agent call.
- *Options:* (a) Groundedness (no invented facts) + brevity/one-imperative + natural Russian; (b) that
  set + actionability (is it a usable instruction) + tone; (c) a single holistic "would a coach say this"
  score.
- *Recommendation:* **(b)** — groundedness, brevity/one-imperative, natural Russian, actionability, tone —
  because it directly encodes the exact failures this pack targets (raw numbers, transliteration,
  multi-imperative), with per-dimension floors possible.

**M18-judge — Which model is the LLM judge?**
- *Why:* release policy + cost — a same-family judge risks self-preference/leniency; the judge choice sets
  the credibility and the per-release cost of the gate.
- *Options:* (a) `anthropic/claude-sonnet-4.6` (already configured, strong RU); (b) a different-family
  model (e.g. Gemini/DeepSeek) to reduce self-preference against the Anthropic-generated candidates; (c)
  a small/cheap model to minimize cost.
- *Recommendation:* **(a) Sonnet 4.6** initially (already wired, strong Russian), anchored by the
  committed known-bad fixtures to catch leniency; revisit a cross-family judge if self-preference shows
  up in the anchors.

**M18-bar — What is the numeric pass bar (composite threshold and any per-dimension floor)?**
- *Why:* release policy — this single number decides whether a prompt edit or model swap ships; it must be
  a deliberate, owner-set line, not a value an agent picks.
- *Options:* (a) Composite-only bar (e.g. ≥ 0.8 of max) with a margin; (b) Composite bar + a per-dimension
  floor (e.g. groundedness must never fall below X); (c) Strict bar with N-sample averaging to reduce
  variance.
- *Recommendation:* **(b)** — a composite bar plus a hard groundedness floor, so a fluent-but-ungrounded
  phrase can never pass; set the exact numbers from a first calibration run against the good + known-bad
  fixtures.

**M18-gate — Does a failing RU-eval *block* a release, or is it advisory-only?**
- *Why:* release policy — the whole point is to block the model swaps, but a hard block on a nondeterministic
  judge can stall releases; the owner must choose the enforcement stance.
- *Options:* (a) Hard gate — a failing run blocks the release; (b) Advisory — the run reports/dumps but does
  not block; (c) Hard gate for the known-bad anchors + advisory for the good-fixture bar (regression-only
  block).
- *Recommendation:* **(c)** — hard-fail if a known-bad anchor scores *above* the bar (proves the judge/scale
  broke) while treating the good-fixture composite as a release-blocking bar only once the number has been
  calibrated and shown stable; keeps nondeterminism from stalling releases while still gating the model swaps.

**M18-reference-judging — Does the judge score candidates blind, or against a committed reference "good"
phrase per fixture?**
- *Why:* affects gate stability and the fixture-authoring burden — reference-anchored judging is far less
  variable but requires committing a canonical RU phrase per fixture (which itself becomes a quality claim).
- *Options:* (a) Blind rubric scoring (no reference phrase); (b) Reference-anchored — judge compares the
  candidate to a committed canonical RU phrase per fixture.
- *Recommendation:* **(b) Reference-anchored** — it materially stabilizes the judge and makes the bar
  meaningful across runs; the canonical phrases live in the committed fixtures alongside the Gold input.
