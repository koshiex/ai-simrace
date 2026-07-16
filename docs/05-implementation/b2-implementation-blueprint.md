# PR-B2 Implementation Blueprint — Beyond-PB Coaching Remainder (P3)

> **Judge verdict: APPROVE_WITH_CHANGES.** The blueprint is architecturally sound; proto
> allocations, the Gold-lockstep cadence model, and the within-PR ordering all check out against
> source. It is **not** implementable as-is: one hard build break (commit 9 omits
> `ComputeSession.cs`, the sole caller of the code whose signatures change) and one
> shipped-wrong-metric hazard (folding the unsigned RMS `racing_line_deviation` into the M36
> dominant-channel argmax). Both are scope/spec edits, not re-architectures. After the **9 folded
> must-fixes** below, the sequence is safe to hand to an implementer.
>
> This document is the implementation-ready companion to
> [`beyond-pb-pr-plan.md`](./beyond-pb-pr-plan.md) (the authoritative reviewed plan — commits 6–17,
> MUST-FIX list, Owner decision points) and [`beyond-pb-reference-status.md`](./beyond-pb-reference-status.md)
> (orientation). It does not restate those; it folds every must-fix into the commit that owns it and
> records the Gold-lockstep plan, the in-game acceptance checklist, and the owner decisions.
>
> **Status: blueprint-only.** No owner greenlight to write code yet (per the reference-status doc).

## Proto allocation — VERIFIED against source

Verified directly against `src/SimCoach.Contracts/Schemas/telemetry.proto`:

- **`AggregatedLoss`** declares only fields **1–5** → **6–12 FREE**.
- **`CornerEvent`** uses **1–20** (max field `exit_line_deviation_m = 20` at proto:118) → **21/22/23 FREE**.
- **`SessionEvent`** uses **1–18 + 21/22** (M46 optimal, merged). Fields **19/20** are held for M41
  **by comment only** (proto:189–190) — there is **no `reserved` keyword**; fields jump 18 → 21 → 22.
  This is a conventional guard, not a compiler-enforced one (see must-fix #9).

All B2 allocations sit in verified-free ranges. The **within-PR ordering 6 → 17 is load-bearing**:
commits 8/9 claim `AggregatedLoss` 6–11 first, which is the only reason commit 14's
`AggregatedLoss 12` is free. **Do not reorder M41 proto (commit 14) ahead of M35/M36** or a
same-PR renumber results. `SessionEvent 21/22` are taken by merged M46 — never reuse.

---

## Per-commit sequence (must-fixes folded)

Each commit ends green: `build + test + format` under `TreatWarningsAsErrors` (IDE0007/0008 `var`
rules and IDE1006 `_camelCase` private fields are **build errors**; `dotnet format` (CI) surfaces
IDE1006). Protobuf codegen must run on the `/mnt/c` drive, never `\\wsl$` UNC paths.

### Commit 6 — M35 · `docs(adr): ADR-0020 AggregatedLoss abs-then-average + falsifiable sum-invariant + cross-unit argmax norm`

Design-of-record for the diagnostic-diff aggregation and the M36 dominant-channel normalization.
Pins the concrete channel set that commit 8's completeness probe and commit 9's argmax key off.

- **Files:** `docs/02-architecture/adr/0020-aggregated-loss-normalization.md`
- **Folds must-fixes:**
  - **#6 rationale** — IOptions cross-unit scales are decision-driving weights, not magic numbers.
  - **[FOLD, MF-2] Argmax domain excludes `racing_line_deviation`.** Pin that the M36 dominant-channel
    argmax compares **only the 3 SIGNED loss channels** (`brake_point`, `throttle_resume`,
    `min_speed`), matching `ChooseReason` (CornerEventBuilder.cs:252–275). `racing_line_deviation` is
    an RMS (CornerEventBuilder.cs:220–244), always `>= 0`; with any positive scale it wins on nearly
    every corner regardless of true loss — an unfalsifiable dominant_channel. It may remain a
    **report-only** diagnostic diff (field 9); the exclusion is on the *argmax domain* only. If line
    shape must ever be a candidate, use the **signed** phase deviations (proto 18–20), never field 9.
  - **[FOLD, MF-3] Define the sum-invariant so it is genuinely falsifiable.** Invariant quantity:
    `aggregate_channel == mean(|per_corner_diff|)`. The sign-fault injection test (commit 8) must
    target a **bidirectional** channel (`brake_point` or `throttle_resume`) on a fixture with at
    least one positive and one negative per-corner diff. For a same-sign channel (`min_speed`,
    negative whenever slower) `abs-then-average == |average-then-abs|`, so a sign swap yields an
    identical number and the assertion cannot fire.
  - **[FOLD, MF-4] Pin the `DeltaMs > 0` conditioning** of the diagnostic-diff averages (see commit 7).
    Recommended semantic: same `DeltaMs > 0` lossy-corner set as `aggregated_losses`, one gate.
- **Tests:** none (docs-only); `dotnet format --verify-no-changes` on the markdown.

### Commit 7 — M35 · `refactor(reference): carry per-channel diagnostic diffs on CornerContribution`

Internal-only refactor: extend the `CornerContribution` record (CornerEventBuilder.cs:13–14) with
the per-channel reference-relative diffs already computed at CornerEventBuilder.cs:131–143
(`brake_point`, `throttle_resume`, `min_speed`, `racing_line_deviation`) so
`SessionLossAccumulator` can aggregate them abs-then-average. Populate in `Build`'s reference
branch; leave at `0` in the no-reference / degenerate branch (mirrors `DeltaMs = 0`).

- **Files:** `src/SimCoach.Reference/CornerEventBuilder.cs`, `src/SimCoach.Reference/SessionLossAccumulator.cs`
- **Folds must-fix #4 (state + pin the conditioning):** the diffs aggregate inside
  `SessionLossAccumulator`, whose `Accept` early-returns on `DeltaMs <= 0` (SessionLossAccumulator.cs:27),
  and `ComputeSession.cs:311` zeroes implausible corners before `Accept` — so `avg_*_diff` are
  conditioned on **lossy corners only**, not a true per-corner average. Document the intended
  semantic (ADR-0020) and add a test pinning it. If an all-corner average is intended instead, route
  the diffs off the gated path.
- **Tests:** abs-then-average per-channel accumulation over multiple corners; diffs populated in the
  reference branch and zero in no-reference/degenerate branch; conditioning-semantic pin test.
- **Gold:** none — `CornerContribution` is an `internal sealed` record, not a Gold surface.

### Commit 8 — M35 · `feat(contracts,reference): AggregatedLoss diagnostic diffs (6-9) + accumulator + falsifiable sum-invariant`

Add the four abs-then-averaged diagnostic-diff fields to `AggregatedLoss` and emit them from
`SessionLossAccumulator.Build` (SessionLossAccumulator.cs:44–64).

- **Proto:** `AggregatedLoss 6,7,8,9` (VERIFIED FREE). `6 = avg_brake_point_diff_m`,
  `7 = avg_throttle_resume_diff_m`, `8 = avg_min_speed_diff_kmh`, `9 = avg_line_deviation_m`.
- **Files:** `telemetry.proto`, `SessionLossAccumulator.cs`, `SessionLossAccumulatorTests.cs`
- **Folds must-fixes:**
  - **#3 (the sum-invariant, made non-vacuous)** — the falsifiable test targets a **bidirectional**
    channel per the ADR-0020 definition; assert mixed signs are present in the fixture (fail-fast) so
    a future same-sign fixture swap reds loudly instead of passing empty.
  - **[FOLD, MF-8] Resolve the orphaned diffs 6–9.** Do **not** leave the "Gold projection deferred
    to commit 10" sentence — commit 10 projects only 10/11. Decide **(a)** fields 6–9 are proto-only
    diagnostic aggregates → the M35 in-game checklist asserts on the `SessionEvent` proto (not Gold);
    or **(b)** add a projection of 6–9 into `GoldAggregatedLoss` within commit 10. Recommended: (a).
- **Tests:** falsifiable sum-invariant (inject sign/unit fault, assert the assertion **fails**);
  completeness probe asserts the **concrete** ADR-0020 channel set (not a count); accumulator emits
  6–9 with abs-then-average semantics; `Phase2ComputeE2EGoldenTests` stays green (new fields default-zero).
- **Gold:** none here — `AggregatedLoss` is non-scalar (rides `GoldAggregatedLoss`), so no
  `GoldFieldNames`/`SampleView` lockstep.

### Commit 9 — M36 · `feat(contracts,reference): dominant channel+value (10-11) via IOptions cross-unit scales`

Replace the deliberately-rough cross-unit argmax (`CornerEventBuilder.ChooseReason:252–275`, raw
`MathF.Max` over metres vs km/h) with a scaled picker. Add ms-per-unit scale fields to
`ComputeOptions` (config-bound plain record, bound at TelemetryComposition.cs:108–119, passed by
value, `EnsureValid` range-check). Per-corner scaled magnitudes flow into `SessionLossAccumulator`
which argmaxes them into `dominant_channel(10)` + `dominant_channel_value(11)`. `dominant_reason(5)`
is **RETAINED** (additive rule) but stops being authoritative.

- **Proto:** `AggregatedLoss 10,11` (VERIFIED FREE). `10 = dominant_channel` (string, closed set),
  `11 = dominant_channel_value` (int, scaled ranking magnitude — see MF-6 on naming).
- **Files:** `telemetry.proto`, `ComputeOptions.cs`, `CornerEventBuilder.cs`,
  `SessionLossAccumulator.cs`, **`ComputeSession.cs`**, `TelemetryComposition.cs`,
  `SessionLossAccumulatorTests.cs`, `ComputeOptionsTests.cs`
- **Folds must-fixes:**
  - **#1 (HARD BUILD BREAK) — add `src/SimCoach.Reference/ComputeSession.cs`.** It is the **sole
    caller** of the static `CornerEventBuilder.Build` (ComputeSession.cs:295–298, passing `_options.*`
    by value) and constructs `_sessionLosses = new()` (ComputeSession.cs:41). Threading the new
    scales through `Build` and/or the accumulator changes those signatures → guaranteed
    **CS7036/CS1501** without editing `ComputeSession`. As originally scoped the commit cannot reach
    green. `SessionLossAccumulator`/`CornerEventBuilder` gain the scale params via ctor/signature
    change (both are `new()`/static today).
  - **#6 (M36 scales are IOptions)** — documented defaults + `config-flips-the-pick` test.
  - **[FOLD, MF-2] Argmax domain = 3 signed channels only** (per ADR-0020, commit 6). Add a test: a
    corner with a real signed loss **and** nonzero line-deviation must **not** pick `line_deviation`.
  - **[FOLD, MF-6] `dominant_channel_value` is a ranking magnitude, not an additive time.**
    `value = raw_diff × config-scale`. Do **not** sum it with `total_loss_ms`. Either omit the number
    from the debrief (surface `dominant_channel` + the corner's real `delta_ms`) or name the field so
    it signals a heuristic scaled magnitude, and document in the proto comment that it must never be
    summed with `total_loss_ms`. This is the same "two disagreeing time numbers" hazard the plan's
    own must-fixes (#4 theoretical_best, #7 GridMetrics.TimeAt) flagged.
- **Tests:** config-flips-the-pick (change ONLY a scale, a different channel wins); line-deviation
  **not** picked when a signed loss exists; `ComputeOptions.EnsureValid` range-checks for the new
  scale fields; migrate `SessionLossAccumulatorTests.cs:25` `DominantReason` assertion →
  `dominant_channel` + value; idempotence (same input + scales → stable pick).
- **Gold:** none here (deferred to commit 10). IDE1006: new `ComputeOptions` private/const fields
  must follow `_camelCase` / PascalCase-const.

### Commit 10 — M36 · `feat(coach): render dominant channel+value in debrief`

Project `AggregatedLoss` `dominant_channel/value` into `GoldAggregatedLoss` (new `init` members)
and render channel + value in the debrief `top_losses` "why" field instead of
`ReasonGloss.ToRu(reason)`. Add channel→RU phrasing (`ChannelGloss` sibling to `ReasonGloss` + resx).
Session-debrief-only (`GoldSessionPayload` → `DebriefTemplate`) — bypasses `GoldFieldNames`,
`CornerGoldView`, and `SampleView` (see Gold-lockstep surface B).

- **Files:** `GoldAggregatedLoss.cs`, `GoldArtifactBuilder.cs`, `DebriefTemplate.cs`,
  `ReasonGloss.cs` (+ `ChannelGloss`), `CoachStrings.resx`, `CoachStrings.ru.resx`,
  `DebriefTemplateTests.cs`, `tests/SimCoach.RuEval/Fixtures/debrief-session.json`, `GoldTestData.cs`
- **Folds must-fixes:** MF-6 render decision (channel + real `delta_ms`, or heuristic-named value —
  never sum with `total_loss_ms`); MF-8 resolution consistency (if option (b) was chosen in commit 8,
  the 6–9 projection lands here).
- **Tests:** `top_losses` why-field renders channel+value; regenerate byte-stable
  `debrief-session.json` deterministically; `GoldTestData.cs` updated for new members.

### Commit 11 — M33 · `feat(contracts,reference,coach): CornerEvent brake_release_diff_m (21) + full Corner Gold lockstep`

Reference-relative brake-release-point diff (self vs reference brake-off position, metres) computed
in `CornerEventBuilder` from the brake profile. Reference-relative → **nullable** in Gold, left null
without reference. **Full Corner Gold lockstep (surface A) — the ~70-test SampleView cascade.**

- **Proto:** `CornerEvent 21` (VERIFIED FREE — max field 20 at proto:118).
- **Files:** `telemetry.proto`, `CornerEventBuilder.cs`, `GoldCornerEvent.cs`, `GoldArtifactBuilder.cs`,
  `CornerGoldView.cs`, `GoldFieldNames.cs`, `CoachStartupValidator.cs`, `actionRegistry.json`,
  `CoachStrings.resx`, `CoachStrings.ru.resx`
- **Tests:** builder computes diff vs reference, 0 in no-reference branch; `GoldFieldNamesTests`
  `_corner` Contain; `GoldHasReferenceDropTests` **NotContain** (reference-relative); 
  `CoachStartupValidatorTests` — `SampleView(Corner)` resolves the new field **non-null** (guards the
  ~70-test cascade); `ActionRegistryLoadTests` count bump + globally-unique `(phase,rank)` if an
  action is added (see D-M33-ACTIONS).

### Commit 12 — M33 · `feat(contracts,pipeline,reference,coach): CornerEvent brake_lockup_score (22) + full Corner Gold lockstep`

Self-derived `[0,1]` brake-lockup score via a new Pipeline kernel, wired through
`CornerEventBuilder`. Self-derived → **always-present** in Gold (non-nullable, no `hasRef` gate).
Full Corner Gold lockstep.

- **Proto:** `CornerEvent 22` (VERIFIED FREE).
- **Files:** `telemetry.proto`, `BrakeLockupKernels.cs`, `CornerEventBuilder.cs`, `GoldCornerEvent.cs`,
  `GoldArtifactBuilder.cs`, `CornerGoldView.cs`, `GoldFieldNames.cs`, `CoachStartupValidator.cs`,
  `actionRegistry.json`, `CoachStrings.resx`, `CoachStrings.ru.resx`
- **Folds must-fix #7 (specify the input signal concretely).** The blueprint's
  "`slip_ratio field 43` / `wheel_slip field 20`" leaves the load-bearing signal undefined.
  `wheel_slip(20)` is a combined magnitude that **cannot** distinguish a locked (under-rotating) wheel
  from wheelspin (proto:46 vs :81–83). Use **FRONT-wheel** (indices 0,1) `slip_ratio(43)` in the
  locking sign with `brake_pct` high, and **attenuate for `abs_active(30)` / `abs(40)`** since an
  ABS-equipped GT3 rarely fully locks. Data availability confirmed: `AccFrameMapper.cs:147` populates
  all four `slip_ratio` entries.
- **Tests:** **table-driven** kernel test distinguishing **locked-front vs exit-wheelspin vs
  ABS-modulated** (a bare "score in [0,1]" passes for any constant and proves nothing);
  `GoldFieldNamesTests` `_corner` Contain; `GoldHasReferenceDropTests` **Contain** (always-present);
  `SampleView(Corner)` non-null; `ActionRegistryLoadTests` count bump if action added.

### Commit 13 — M33 · `feat(contracts,pipeline,reference,coach): CornerEvent short_shift_score (23) + full Corner Gold lockstep`

Self-derived `[0,1]` short-shift score from rpm/gear (upshift below the power-band) via a new
Pipeline kernel. Self-derived → always-present in Gold. Full Corner Gold lockstep. Closes the
`CornerEvent` additive block (21/22/23 all consumed).

- **Proto:** `CornerEvent 23` (VERIFIED FREE).
- **Files:** `telemetry.proto`, `ShortShiftKernels.cs`, `CornerEventBuilder.cs`, `GoldCornerEvent.cs`,
  `GoldArtifactBuilder.cs`, `CornerGoldView.cs`, `GoldFieldNames.cs`, `CoachStartupValidator.cs`,
  `actionRegistry.json`, `CoachStrings.resx`, `CoachStrings.ru.resx`
- **Tests:** table-driven kernel (short-shift vs power-band upshift); `GoldFieldNamesTests` `_corner`
  Contain; `GoldHasReferenceDropTests` **Contain**; `SampleView(Corner)` non-null;
  `ActionRegistryLoadTests` count bump if action added.

### Commit 14 — M41 · `feat(contracts): SessionEvent 19/20 + AggregatedLoss 12 + new messages`

Proto-only additive change. Adds two repeated message fields to `SessionEvent` (19/20 — held for
M41 at proto:189–190), one repeated message field to `AggregatedLoss` (12), and the three new
top-level messages. **Must land after commits 8/9 have claimed `AggregatedLoss` 6–11** so 12 does
not collide.

- **Proto:** `SessionEvent 19 = repeated SectorCornerMembership sector_corner_membership`,
  `20 = repeated BalancePhaseTrend balance_phase_trend`; `AggregatedLoss 12 = repeated LossTrend loss_trend`;
  new messages `SectorCornerMembership`, `BalancePhaseTrend`, `LossTrend` (all non-scalar/repeated).
- **Files:** `telemetry.proto`
- **Folds must-fix #9 (make the 19/20 reservation compiler-enforced).** Add an enforced
  `reserved 19, 20;` statement to `SessionEvent` as a **preamble before this commit** (fields
  currently jump 18 → 21 → 22 with no `reserved` keyword, so a concurrently-merged proto edit could
  legally claim them), then **remove it in commit 14** as it consumes 19/20. This guards only the
  SessionEvent window; the `AggregatedLoss 6–12` ordering remains a manual discipline (residual risk).
- **Tests:** codegen compiles; `Phase2ComputeE2EGoldenTests` stays green (new fields default empty/zero).

### Commit 15 — M41 · `feat(reference): per-phase balance trend, per-corner loss trend, grounded sector→corner membership`

Produce the M41 aggregates in `ComputeSession.Complete` (ComputeSession.cs:180–217).
(1) Per-phase (entry/apex/exit) balance: score `BalanceKernels` per `CornerPhaseBands` band (the
reusable seam today only wired to line deviation) → `BalancePhaseTrend` — a genuinely new kernel
path, not a rewiring of `understeer_trend(11)`. (2) Per-corner loss trend: lap-indexed series →
`LossTrend` on `AggregatedLoss 12`. (3) Grounded sector→corner membership: derived at session end by
intersecting observed sector-cross positions (`current_sector_index`, EmitSector:382–383) with baked
corner apex positions — sectors stay OUT of the track model per ADR-0010; membership is derived, not
persisted (D-M41-MEMBERSHIP).

- **Files:** `BalanceKernels.cs`, `CornerPhaseBands.cs`, `CornerEventBuilder.cs`,
  `SessionLossAccumulator.cs`, `ComputeSession.cs`, `Phase2ComputeE2EGoldenTests.cs`, `BalanceKernelsTests.cs`
- **Tests:** per-phase balance kernel (entry/apex/exit scored independently, table-driven);
  loss-trend lap-indexed series ordering; membership matches baked apex ∩ observed sector-cross
  ranges; `Phase2ComputeE2EGoldenTests` asserts `SessionEvent 19/20` + `AggregatedLoss 12` **non-empty**
  on synthetic Spa (lapCount:4) — the in-repo structural golden backing the manual replay check.

### Commit 16 — M41 · `feat(coach): M41 trend/membership/balance in Gold session payload + grounded setup_hint + Gold lockstep`

Map `SessionEvent 19/20` into `GoldSessionPayload` as **non-scalar** members and synthesize a
grounded `setup_hint` in `GoldArtifactBuilder.BuildSession` (replacing the hardcoded null at :105)
from the per-phase balance grounds. Render for the LLM-off path in `DebriefTemplate`.

- **Files:** `GoldSessionPayload.cs`, **`GoldAggregatedLoss.cs`**, `GoldArtifactBuilder.cs`,
  `DebriefTemplate.cs`, `GoldFieldNames.cs`, `CoachStrings.resx`, `CoachStrings.ru.resx`,
  `GoldFieldNamesTests.cs`, `DebriefTemplateTests.cs`, `tests/SimCoach.RuEval/Fixtures/debrief-session.json`
- **Folds must-fixes:**
  - **#5 — add `GoldAggregatedLoss.cs` (+ the `GoldArtifactBuilder.AggregatedLosses` path ~:142–153)
    to this commit and correct the wording.** `loss_trend` rides `AggregatedLoss` field 12
    (per-corner, D-M41-ALLOC), so it must project into **`GoldAggregatedLoss`** (the per-corner Gold
    record, already touched by commit 10 for `dominant_channel`), **NOT `GoldSessionPayload`**. Only
    `SessionEvent 19/20` map to `GoldSessionPayload`. As originally scoped, `loss_trend` never reaches
    Gold JSON and the checklist item "`loss_trend(12)` has a multi-lap series" is unverifiable.
  - Gold-lockstep note (surface C): the new `GoldSessionPayload` members are **non-scalar** →
    auto-EXCLUDED from the reflected `GoldFieldNames._session` drift guard AND from `SampleView`
    (Session cadence has ZERO registry actions; `SampleView(Session)` throws NotSupported — the
    ~70-test cascade does NOT apply). grounded `setup_hint` stays under the existing (name-excluded)
    `SetupHint` member. **Any new session-level SCALAR** added here would auto-fail the reflected
    `GoldFieldNamesTests` → must update `GoldFieldNames._session` (GoldFieldNames.cs:38–42) in lockstep.
- **Tests:** reflected `_session` test still passes with only non-scalar additions;
  `BuildSession` golden — new members + grounded `setup_hint` populated; `DebriefTemplateTests` +
  regenerate `debrief-session.json`; grounded `setup_hint` present only when balance grounds exist
  (else null, matching the prompt contract).
- **Owner gate:** **D-GOLD-SIGNOFF = SIGNED (owner, 2026-07-16).** The M41 aggregate fields may
  leave the machine in Gold JSON; aggregates-only, no raw telemetry (privacy rule) — same class as
  the merged M46 optimal_* sign-off.

### Commit 17 — M39 · `feat(llm): RouteOptions.CacheSystemPrompt IOptions flag (default off)` — **INCLUDED (owner, D-M39)**

Owner resolved **D-M39 = INCLUDE**. Expose `CacheSystemPrompt` as an IOptions prefix-minimum
(default off) + a test asserting the flag toggles a `cache_control` marker on the outbound request.
No behaviour change when off; pure metering-prep now wired.

- **Files:** `RouteOptions.cs`, `TelemetryComposition.cs`, LLM router + a `cache_control`-marker toggle test.

---

## Gold-lockstep plan

Three distinct lockstep surfaces, keyed by **cadence**. Do **not** apply the Corner recipe uniformly.

### (A) CORNER-cadence scalar — M33 commits 11/12/13 (the ~70-test cascade path)

Every new `CornerEvent` field a clause can read must sync **6 files in ONE commit**:

1. **`GoldCornerEvent.cs`** — add as an `init` member, **NOT** a positional ctor param (preserves the
   positional shape fixtures use); reference-relative → nullable, self-derived → non-nullable.
2. **`GoldArtifactBuilder.BuildCorner`** — populate in the `{}` init block; gate reference-relative
   fields on `hasRef`.
3. **`CornerGoldView.TryGetNumber/Bool/String`** — add `case "<field>":` (switch must match the
   catalog EXACTLY).
4. **`GoldFieldNames._corner`** (GoldFieldNames.cs:14–21) — add the field-name string.
5. **`CoachStartupValidator.SampleView(Corner)`** (cs:156–164) — set the new field **NON-NULL** in the
   positional-ctor init block, else the startup check fails for **every** Corner action → ~70-test
   `CoachStartupValidatorTests` cascade (the failure message names the field, **not** the fix site).
6. **`GoldHasReferenceDropTests`** — reference-relative → **NotContain**; always-present → **Contain**.

Plus, if a new `actionRegistry.json` action reads it: bump `ActionRegistryLoadTests` hardcoded action
count, pick a **globally-unique** `(phase,rank)`, add `hint_ru`/`hint_en` + `phrase_template_ru` + resx.

### (B) SESSION-DEBRIEF non-scalar — M35/M36 `AggregatedLoss` dominant/diffs, commits 8–10 & 16

`AggregatedLoss` `dominant_channel/value` (and `loss_trend`) ride `GoldAggregatedLoss` (a per-corner
record inside the non-scalar `aggregated_losses` list), reached only via
`GoldSessionPayload → DebriefTemplate`. This **bypasses** `GoldFieldNames`, `CornerGoldView`, and
`SampleView` (grep of `GoldFieldNames` for dominant/reason is empty). Sync: proto field →
`SessionLossAccumulator` producer → `GoldAggregatedLoss` member →
`GoldArtifactBuilder.AggregatedLosses` (:142–153) → `DebriefTemplate` render + `ReasonGloss`/`ChannelGloss`
+ resx. Regression net = byte-stable golden `debrief-session.json` (regenerate) + `GoldTestData.cs`.

### (C) SESSION-cadence Gold — M41 commit 16

Session has ZERO registry actions → `SampleView(Session)` **throws NotSupported** (asserted) → the
~70-test cascade does **not** apply. `GoldFieldNamesTests` reflects `GoldSessionPayload`'s **scalar**
props (`IsScalar` = primitive/string/decimal). Therefore: **non-scalar** additions (the 3 M41
messages as lists/records) are auto-EXCLUDED like `aggregated_losses`/`stints` — no `_session` edit,
but also **no auto drift protection**, so `BuildSession`/`Phase2ComputeE2EGolden` fixtures are the
net. **Any new SCALAR** on `GoldSessionPayload` auto-RAISES the reflected expectation and FAILS until
`GoldFieldNames._session` (GoldFieldNames.cs:38–42) is updated by hand. grounded `setup_hint` is
EXCLUDED by the `SetupHint` name-exclusion (verified GoldFieldNamesTests.cs:67) — keep that name.

---

## In-game acceptance checklist

Decision-#3 in-game acceptance: the diagnostic payload is the least-observable surface, so it gets a
concrete non-zero verification step on a known replay.

**REPLAY:** session `20260704-132800-625` (Monza / bmw_m4_gt3 / dry-warm, 5 laps / 4 CLEAN, pb
113432ms) — the most-clean-laps recording, so understeer/balance trends and dominant_channel
aggregates are firmly non-zero and multi-sample. Located at
`%LOCALAPPDATA%/SimCoach/recordings/20260704-132800-625` (7 mcap segments + laps.parquet). Backups
(also Monza/BMW clean): `20260705-130034-345`, `20260704-100134-252`, `20260701-151452-346`. **AVOID**
single-clean-lap sessions (`20260710-194431-033`, `20260715-195743-365`, `20260701-171602-738`) —
thin/zero trend, no cross-session deficit spread.

Run (WSL uses `"/mnt/c/Program Files/dotnet/dotnet.exe"`):

```
SIMCOACH_Telemetry__Source=replay \
SIMCOACH_Telemetry__Replay__Path=/mnt/c/Users/<user>/AppData/Local/SimCoach/recordings/20260704-132800-625 \
dotnet run --project src/SimCoach.App
```

Checklist — assert on the emitted `SessionEvent` / Gold debrief JSON:

- [ ] **M36:** `AggregatedLoss.dominant_channel` is a non-empty closed-set value AND
      `dominant_channel_value > 0` on ≥1 aggregated loss (NOT the empty/"slower" fallback), and the
      channel is one of the **3 signed** channels — never `line_deviation` (MF-2).
- [ ] **M36:** flipping a `ComputeOptions` scale in config changes the observed `dominant_channel` on
      the SAME replay (config-flips-the-pick, live).
- [ ] **M35:** the four per-channel diagnostic diffs (`AggregatedLoss` 6–9) are populated on the
      **`SessionEvent` proto** and the sum-invariant holds on real data. (Assert on the proto, not
      Gold, per MF-8 option (a).)
- [ ] **M41:** `SessionEvent.understeer_trend(11)` non-zero AND `balance_phase_trend(20)` populated
      with per-phase (entry/apex/exit) values, not a single scalar.
- [ ] **M41:** `sector_corner_membership(19)` maps each observed sector to ≥1 corner;
      `AggregatedLoss.loss_trend(12)` (via `GoldAggregatedLoss`, MF-5) has a multi-lap series.
- [ ] **M41:** grounded `setup_hint` is populated (устойчивый снос/занос grounded in balance) — NOT null.
- [ ] **M33:** `brake_release_diff_m(21)` non-zero on ≥1 corner with a reference;
      `brake_lockup_score(22)` shows a **meaningful non-zero** score (verify the ABS attenuation does
      not floor it on the BMW-M4-GT3 replay); `short_shift_score(23)` in `[0,1]`.
- [ ] **Automated complement:** `Phase2ComputeE2EGoldenTests` (synthetic Spa, lapCount:4) asserts the
      new `SessionEvent 19/20` + `AggregatedLoss 12` fields non-empty — the in-repo structural golden
      backing this manual replay check.

---

## MUST-FIX (folded)

Nine must-fixes from the Judge, each folded into the owning commit above.

| # | Sev | Commit(s) | Fold |
|---|-----|-----------|------|
| 1 | HIGH | 9 | Add `ComputeSession.cs` — sole caller of `CornerEventBuilder.Build` + constructor of `SessionLossAccumulator`; threaded scales are a hard CS7036/CS1501 without it. |
| 2 | HIGH | 6 + 9 | Exclude unsigned RMS `racing_line_deviation` from the M36 argmax **domain** (3 signed channels only); it may stay a report-only diff. Add a test it is not picked when a signed loss exists. |
| 3 | HIGH | 6 + 8 | Make the sum-invariant non-vacuous: define `aggregate == mean(|per_corner_diff|)`, inject the sign fault on a **bidirectional** channel with mixed-sign fixture, assert mixed signs present. |
| 4 | MED | 6 + 7 | State/pin the `DeltaMs > 0` conditioning of the diagnostic-diff averages (lossy-corner set); document in ADR-0020, add a pin test. |
| 5 | MED | 16 | Add `GoldAggregatedLoss.cs` (+ `AggregatedLosses` path) — `loss_trend(12)` projects to `GoldAggregatedLoss` (per-corner), NOT `GoldSessionPayload`; correct the wording. |
| 6 | MED | 9 + 10 | `dominant_channel_value` is a ranking magnitude, not an additive time — omit from debrief or rename to signal heuristic; never sum with `total_loss_ms`. |
| 7 | MED | 12 | Specify `brake_lockup_score` signal: **front-wheel** `slip_ratio(43)` in locking sign + `brake_pct`, attenuate for `abs_active(30)`/`abs(40)`; table-driven test locked-front vs wheelspin vs ABS. |
| 8 | MED | 8 + 10 | Resolve the orphaned diffs 6–9: declare proto-only (checklist asserts proto) OR project into `GoldAggregatedLoss` in commit 10. Remove the false "deferred to commit 10" sentence. |
| 9 | LOW | 14 | Add enforced `reserved 19, 20;` to `SessionEvent` as a pre-commit-14 preamble, removed in commit 14 as it consumes them (proto:189–190 protects by comment only). |

---

## Owner decision points

**RESOLVED (owner, 2026-07-16):**
- **D-M39 → INCLUDE.** M39 stays in PR-B2 as commit 17 (IOptions prefix-minimum, default off + cache_control-marker toggle test).
- **D-GOLD-SIGNOFF → SIGNED.** M41 aggregate fields (balance-phase trend, sector→corner membership, loss trend, grounded setup_hint) may leave the machine in Gold JSON; aggregates-only, no raw telemetry.
- **D-M33-ACTIONS → VOICE.** The 3 M33 channels get full coach actions (brake-release-too-early, brake-lockup, short-shift) with RU phrasing + resx; bump `ActionRegistryLoadTests` count, globally-unique `(phase,rank)`.

The remaining rows are **implementer-defaults** (review-backed, taken as recommended unless the owner reopens).

| ID | Decision | Recommendation | Status |
|----|----------|----------------|---|
| **D-M39** | Ship M39 `RouteOptions.CacheSystemPrompt` in PR-B2 (commit 17) or defer? | IOptions prefix-minimum (default off) + cache_control-marker toggle test. | ✅ **INCLUDE** |
| **D-GOLD-SIGNOFF** | Sign-off for M41 aggregate fields leaving the machine in Gold JSON? | Aggregates-only, no raw telemetry — same class as the merged M46 optimal_* sign-off. | ✅ **SIGNED** |
| **D-CHANNELSET** | M35 diagnostic-diff channel set / field count, AND the M36 argmax domain (3 vs 4)? | Diffs 6–9 = {brake_point, throttle_resume, min_speed, line_deviation} (4, **report-only**). M36 argmax **domain = 3 SIGNED** channels only (matches ChooseReason). Unsigned RMS line_deviation would spuriously win the argmax (MF-2). Pin both in ADR-0020. | ☐ |
| **D-M41-GOLD-SCALAR** | Do any M41 additions introduce a new session-level SCALAR on `GoldSessionPayload`? | Keep all 3 M41 additions **non-scalar** (excluded like aggregated_losses/stints); synthesize grounded setup_hint under the existing name-excluded `SetupHint` member; add no new `_session` scalar. Any new scalar reds the reflected `GoldFieldNamesTests` until `_session` is hand-updated. | ☐ |
| **D-SETUPHINT** | Grounded setup_hint — new proto scalar or Gold-layer synthesis under existing `SetupHint`? | Gold-layer synthesis in `GoldArtifactBuilder.BuildSession` from the per-phase balance grounds; keep the `SetupHint` name (drift-guard-excluded, GoldFieldNamesTests.cs:67). No new proto scalar; trips no test. | ☐ |
| **D-M41-MEMBERSHIP** | How is grounded sector→corner membership derived, given ADR-0010 keeps sectors out of the track model? | Derive at session-end by intersecting observed sector-cross positions (runtime `current_sector_index`, EmitSector) with baked corner apex positions; emit `SectorCornerMembership` WITHOUT persisting a boundary map into `TrackModel`. | ☐ |
| **D-DOMREASON-5** | Does M36 delete/deprecate `dominant_reason` (AggregatedLoss field 5)? | **RETAIN** field 5 (additive rule forbids reuse/removal); stop treating it as authoritative and stop rendering it, drive coaching from `dominant_channel(10)`+`value(11)`. Confirm: keep-populating for back-compat vs deprecate-comment. | ☐ |
| **D-M41-ALLOC** | Message-to-field assignment across `SessionEvent 19/20` and `AggregatedLoss 12`? | `SessionEvent 19 = SectorCornerMembership`, `20 = BalancePhaseTrend`; `AggregatedLoss 12 = LossTrend` (per-corner loss nests on the per-corner message). Confirm — it fixes the Gold projection target (12 → GoldAggregatedLoss, MF-5). | ☐ |
| **D-M33-ACTIONS** | Do the 3 M33 channels (21/22/23) get new actionRegistry coach actions with RU phrasing, or land Gold-only? | Add actions (brake-release-too-early, brake-lockup, short-shift) with hint_ru/hint_en + phrase_template_ru + resx; bump `ActionRegistryLoadTests` count + globally-unique `(phase,rank)`. | ✅ **VOICE** |
| **D-B2-SPLIT** | Keep PR-B2 as one PR or split B2a(M33)/B2b(diagnostics)? | **KEEP ONE PR-B2** (already owner-ratified) with per-commit Strict→Defender→Judge and the in-game checklist. The reference-status doc's "likely split" note is superseded. | ☐ |

> **D-ARGMAX-LAYER** (which layer owns the scaled argmax) is resolved to an implementer-internal
> layering choice — see residual risks. Recommended default: `CornerEventBuilder` emits per-channel
> **scaled** magnitudes; `SessionLossAccumulator` sums + argmaxes them into `dominant_channel/value`.

---

## Residual risks

- **D-ARGMAX-LAYER** is now pure internal layering — the correctness dimension is covered by MF-2
  (line-deviation exclusion) and MF-6 (scaled magnitude). Emitting scaled per-channel magnitudes
  per-corner and argmaxing them in the accumulator is a safe default with no external-contract impact.
- **`brake_lockup_score(22)` / `short_shift_score(23)`** are new `[0,1]` heuristics with
  model/car-dependent thresholds and no ground truth; the checklist "in [0,1]" is trivially satisfied.
  Acceptance rests on the discriminating table-driven tests (MF-7) plus config-driven tuning that
  stays unvalidated until real in-game running.
- **ABS-equipped GT3 cars rarely fully lock**, so an ABS-attenuated `brake_lockup_score` may
  under-report on the recommended BMW-M4-GT3 replay; verify a meaningful non-zero score is observable
  on `20260704-132800-625` before relying on the checklist item.
- **Replay-session existence** (`20260704-132800-625` + backups at `%LOCALAPPDATA%/SimCoach/recordings`)
  is not verifiable from the review environment. If absent, the sole non-zero acceptance surface for
  the least-observable diagnostic payload is lost — confirm before coding.
- **Within-PR proto ordering (6 → 17)** is what keeps `AggregatedLoss 12` free; a reorder landing M41
  proto (commit 14) before M35/M36 claim 6–11 causes a same-PR renumber. The enforced `reserved 19,20;`
  (MF-9) guards only the SessionEvent window, not the AggregatedLoss 6–12 ordering — that remains a
  manual guard.
- **PR-B2 is large** (12 kept commits, 3 milestones, all three Gold-lockstep cadences). The atomic
  6-file Corner lockstep (commits 11/12/13) is the highest-risk mechanical surface — a single missed
  `SampleView(Corner)` non-null cascades ~70 `CoachStartupValidator` tests whose failure message names
  the field, not the fix site.
- **The sum-invariant remains dependent on test-authoring discipline** even after MF-3: a future
  fixture swap to a same-sign channel silently reverts the guard to vacuous. Assert the mixed-sign
  precondition inside the test body so it fails loudly rather than passing empty.
