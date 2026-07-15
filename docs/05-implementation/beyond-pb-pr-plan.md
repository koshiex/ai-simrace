# Beyond-PB next PR set — reviewed implementation plan (PR-B1 / B2 / B3)

**Status: plan approved-with-changes, blueprint-only. No code written.** This is the synthesized plan for the
next PR set (own-optimal + coaching remainder + ghost alien-line), after a Strict → Defender → Judge review.
It supersedes the single-PR framing in `m46-optimal-reference-plan.md` (which stays valid as the M46 detail
doc). Ghost format detail: `acc-ghost-format-re.md`. Orientation map: `beyond-pb-reference-status.md`.

## Judge verdict: APPROVE-WITH-CHANGES

The three-PR split and the B1 → B2 → B3 dependency order stand; the LINE-only ghost decision stands; **no
re-architecture.** But the plan ships with **1 critical + 7 high/medium must-fixes** that must be folded into
the plan text and commit scope **before any code is written**. The must-fixes are amendments, not a redesign.

## PR split (three sequential PRs)

- **PR-B1 = M46 own-optimal — FOUNDATION, merges first.** Introduces the reference-`kind` mechanism the ghost
  line later reuses: migration `007_reference_kind` (rebuild `[references]` with `kind TEXT NOT NULL DEFAULT
  'pb'` + `sector_sources_json`, `UNIQUE(track,car,weather,kind)`), `ReferenceKind` enum, `ReferenceRow.Kind/
  .SectorSourcesJson`, kind-aware `ReferenceRepository` upsert + `ReferenceLookup.Get(triple,kind)`, plus the
  own-optimal builder/baker and TIME-only delta routing. **Nothing beyond-PB ships without this.**
- **PR-B2 = P3 coaching remainder (M33, M35, M36, M41, [M39 — likely deferred]).** The owner-ratified "PR-B"
  from the P3 plan. Independent of M46/ghost at runtime, but it ALSO extends `SessionEvent` (M41 → fields
  19/20), so its proto numbers must reconcile against M46's `SessionEvent` additions. Opens **after** B1
  merges so only one live PR extends `SessionEvent` at a time.
- **PR-B3 = Ghost alien-LINE — gated, last by default.** Hard-depends on B1 (needs the `kind` mechanism). Adds
  `ReferenceKind.AlienLine`, the offline tool `tools/SimCoach.GhostImport` (the substantial new code), a
  seam-mask, and a ~3-line `ComputeSession.InitSession` change to prefer the `alien_line` reference for
  `_lineReference`. **No proto change** (`CornerEvent` 9/18/19/20 already carry the line cues).

**Corrected sequencing rationale (the plan's original reason was partly false):** B3 does NOT contend with B2
on `SessionEvent` — B3's proto is UNCHANGED. Strike the "two contract-change tracks push SessionEvent from two
live PRs" reason for putting B3 after B2. The only real B3 constraint is **B3 → B1** (kind mechanism). B3 is
last *solely* because it carries the unresolved owner gate (single-ghost vs consensus, provisional decode) and
must not block the ratified coaching work; **it MAY move ahead of B2 if the owner resolves that gate.** The
genuine proto contention is B1-vs-B2 (both add `SessionEvent` fields), handled by the additive-map
reconciliation (owner decision below).

## MUST-FIX before any code (Judge, 1 critical + 7 high/medium — all confirmed against source)

1. **[CRITICAL · PR-B3] Seam-bin suppression needs a real mechanism.** "DISCARD the two seam bins" has zero
   enforcement path — the alien `ResampledLap`'s only consumer, `GridMetrics.InterpWorldXZ/Tangent`,
   interpolates every bin blindly, and Parabolica (pn 0.92–1.00) IS a real corner that WILL raise a
   `CornerEvent`. Two failure modes both fabricate advice: zeroing those bins → `InterpWorldXZ` interpolates
   toward `(0,0)` → garbage multi-hundred-metre deviation; keeping the raw single-ghost line there → coin-flip
   noise (std 2.1 m, 89 % sign-agree). **Fix:** add an explicit per-bin **validity mask** to the alien
   reference (a `bool[]` or NaN sentinel); `SignedLineDeviation.MedianSignedOffset` skips masked bins and
   `CornerEventBuilder` sets `lineRelevant=false` (emit `0f` deviation, reusing the existing short-circuit at
   `CornerEventBuilder.cs:165`) when a corner's entry/apex/exit band falls in a masked pn range. Add a
   suppression test asserting no line-deviation cue fires in masked ranges. The existing line-only guard
   (zeroed time/speed + `_lineReference`-only) does NOT cover this — it prevents TIME leakage, not fabricated
   LINE deviations.

2. **[HIGH · PR-B1] Debrief must reflect the session it debriefs.** Baking the optimal only at `StartAsync`/
   `StopAsync` means the debrief's `optimal_gap_ms` is **empty on the first-ever session** for a triple and
   **stale thereafter** (excludes today's laps) — the headline feature is dark exactly when wanted. **Fix:**
   compute the debrief's sector-deficit at session-end from LIVE best-of-session sectors merged with the
   persisted cross-session optimal (reuse the per-sector best path that already feeds field 16); `gap = PB −
   min(persisted-optimal-sectors, this-session-sectors)`. First-session fallback: show the within-session
   theoretical best and OMIT `optimal_gap_ms` rather than render empty. (Also removes the baker from the
   load-bearing stop-order chain — see #8-baker.)

3. **[HIGH · PR-B1] Per-sector outlier guard, not a per-lap sum window.** The owner-gated `s1+s2+s3 ≈
   lap_time` window can only reject a single internally-inconsistent lap; it is structurally incapable of
   catching the actual poisoning mode — best-S1-from-lap-A stitched with best-S3-from-lap-C being an
   unreachable combination (tow / undetected cut / grip spike on one sector). **Fix:** reject a sector-best
   candidate sitting more than a config `MaxSectorOutlierMs` (or N robust-stddev) below that sector's
   clean-time distribution. Keep the per-lap sum check only as a cheap cut/timing-glitch filter. Re-scope
   owner-decision-point 1 to the per-sector distribution guard; it gates PR-B1 commit 3, not commit 1.

4. **[HIGH · PR-B1] Reconcile the duplicate "theoretical best" metric.** `SessionEvent` field 16
   `theoretical_best_gap_ms` **already ships** (within-session best sectors, `DebriefTemplate.cs:55`,
   `GoldFieldNames.cs:41`). M46's cross-session `optimal_gap_ms` lands a SECOND "theoretical best" with a
   different baseline into the same `SessionEvent`/Gold/debrief — the LLM gets two disagreeing numbers with no
   disambiguation. **Do not stack them.** Treat cross-session optimal as SUPERSEDING: either demote the
   within-session number to a first-session-only fallback, OR give both unambiguous Gold names + debrief
   labels ("theoretical best this session" vs "all-time optimal") PLUS a one-line system-prompt note on their
   relationship. Reconcile in the same commit that adds `optimal_gap_ms` (PR-B1 commit 5), jointly with #2.

5. **[HIGH · PR-B3] Reused fields = changed coaching meaning.** Fields 18/19/20 (`entry/apex/exit_line_
   deviation_m`) and the M38 registry/templates were authored/tuned for **self-median** deviation (~0–1 m,
   "you drifted from your usual line"). Fed a **2–4 m alien corridor** the SAME number means "you're off a
   faster line — move toward it" — a different intent — and the M38 relevance gate (radius ceiling + LateralG
   neutralisation) + RU phrasing were calibrated for the small-deviation regime. "Zero new coaching code" is
   false as stated. **Fix:** PR-B3 must include (a) a **config-driven** review/adjustment of the M38
   relevance-gate thresholds for sustained multi-metre offsets, and (b) distinct RU template phrasing for the
   alien-line regime OR explicit confirmation the existing phrasing reads correctly at 2–4 m. Message-registry
   edit, not new kernel code — so it stays consistent with the "reuse the seam" claim.

6. **[MED · PR-B2] M36 normalization scales must be `IOptions`.** The per-unit scales that decide which
   channel wins the "dominant cause" argmax are decision-driving weights, and "all thresholds config-driven,
   no magic numbers" is an enforced repo rule. **Fix:** expose them as an `IOptions<T>` record (documented
   defaults) + a test that flips the dominant pick by changing only config. Land in the M36-dominant commit.

7. **[MED · PR-B1] Store optimal as sector-boundary times only; don't materialize a per-metre TIME grid.** A
   stitched grid has non-physical intra-sector `t_ms` and `TimeAt` (`GridMetrics.cs:46`) interpolates, so any
   non-boundary query fabricates a time — the "never `SliceToFrames`" assert doesn't constrain WHERE `TimeAt`
   is read. **Fix:** persist only the three cumulative sector-boundary times as the optimal payload; read
   optimal deltas exclusively at sector-cross positions (`ComputeSession` already tracks `_prevSectorCrossPos`
   at `ComputeSession.cs:301`). Add a test asserting the optimal reference is queried only at boundaries and
   mid-sector `TimeAt` is never called. Removes the fabricated-time surface instead of guarding by convention.

8. **[MED · PR-B1] Fully specify migration 007.** The UNIQUE-key change forces a 12-step SQLite table rebuild
   inside the single migrator transaction (`DatabaseMigrator.cs:33`) where `PRAGMA foreign_keys=OFF` is a
   no-op. Survivable (nothing FK-references `references.id`; its lone outgoing FK `source_session_id` is
   recreated), but must be spelled out in the commit: (a) SELECT-copy `id`/`pinned`/`created_at` and all
   existing columns preserving values, (b) set `kind='pb'` on copied rows, (c) run `PRAGMA foreign_key_check`
   as a query at end-of-migration and fail on any rows, (d) `Migration007Tests` covering fresh-create and
   006→007 upgrade with a populated row surviving with identity intact. Replace "rebuild `[references]`" with
   these steps.

### Also-fold-in (minor, from Defender — not blocking but cheap and correct)

- **[PR-B1 baker]** Do NOT insert `OptimalReferenceBaker` into the load-bearing reversed stop-order for the
  debrief path. Rebuild the persisted optimal as an idempotent one-shot at `StartAsync` catch-up (off the hot
  path); the debrief deficit comes from live session bests + persisted optimal (#2). If a post-session persist
  write is still wanted, drive it from a `SessionEvent`-end subscription, not hosted-service ordering. Keep the
  registration-order test. Re-scope PR-B1 commit 4.
- **[PR-B1 doc]** Soften the M46 "non-circular" claim to "each sub-target physically driven **under matching
  weather**"; tie the YAGNI deferral of a recency window explicitly to the per-sector outlier guard shipping.
- **[PR-B2/B3 Gold lockstep]** `CoachStartupValidator` hard-fails at startup on any registry action
  referencing a Gold field `SampleView` can't resolve non-null. Add the same lockstep note (Gold record +
  `GoldFieldNames` + positional `Gold*Event` constructor + `SampleView` non-null + golden fixture, all in one
  commit) to the **M33 coach commits AND the M41-goldpayload commit**, not just M46.
- **[PR-B2 M35 tests]** Make the sum-invariant test FALSIFIABLE (inject a sign/unit fault, assert it fails);
  make the completeness probe assert a concrete channel set or delete it. No coverage theater.
- **[PR-B3 specimen]** Use an **owner-produced / anonymized** `.ghost` for the decode unit test — NO
  third-party `.ghost` committed. Store a driver name in provenance only when present, as optional metadata.

## Ghost architecture fit (how the alien line drops in — validated, LINE-only)

- **Third reference kind `alien_line`** alongside `pb` and `optimal`. Stored exactly like any reference: one
  `[references]` row + one Parquet, using the UNCHANGED `ResampledLap` / `ResampledLapParquet` (world_x/y/z
  already present) / `ReferenceParquetCodec`. Adds NO migration and NO column — just a new value of `kind`.
- **Each kind reads through a different, non-overlapping `GridMetrics` facet** — partial grids are safe by
  construction: `pb` = full grid; `optimal` = TIME-only (`TimeAt`/`[^1]`, never `SliceToFrames`); `alien_line`
  = LINE-only (`InterpWorldXZ`/`InterpWorldTangent`, never `TimeAt`/`SliceToFrames`). Alien Parquet populates
  only `position_normalized`, `world_x`, `world_z`; time/speed/pedal columns stay zero — exactly the shape
  `CenterlineLineReference.Build` already emits for the M38 median centerline.
- **Offline importer `tools/SimCoach.GhostImport`** (mirrors `tools/SimCoach.Bake` — ACC-specific decode kept
  out of the sim-agnostic runtime): decode (container→zlib→130-byte records) → **validate-or-fail** (arithmetic
  `recStart+count*130+11==payloadLen`, world-XZ in track bbox, loop closure) → lap-split by loop closure →
  nearest-point align onto our `pb` centerline (median deviation ≤ ~2 m guard) → per-metre resample →
  LINE-only `ResampledLap` (+ seam mask) → `ReferenceParquetCodec.Write` → `ReferenceRepository.Upsert
  kind='alien_line'` with ghost provenance in `sector_sources_json`.
- **The M38 `lineReference` seam is the whole integration surface — zero new coaching code.**
  `CornerEventBuilder.Build` already takes `ResampledLap? lineReference` (`cs:36`), `SignedLineDeviation` is a
  pure line-source-agnostic kernel, `ComputeSession` already holds `_lineReference`. The ONLY runtime change:
  ~3 lines in `ComputeSession.InitSession` (~L237) to prefer the `alien_line` reference for `_lineReference`,
  else centerline, else null → PB. **This finally makes M38 fire:** the self-median line zeroes out for a
  consistent driver; the alien corridor is a real 2–4 m signed per-corner difference.
- **Precondition:** a `pb` reference (or vendored centerline) must exist for the triple as the alignment
  target. Weather: alien ghosts are Dry, so `weather_bucket` scopes the alien line to dry sessions.

## Full commit sequence (each build+test+format green; per-component Strict→Defender→Judge before PR)

**PR-B1 (M46 own-optimal):**
1. ADR-0021: reference-kind taxonomy (pb=full, optimal=TIME-only, foundation for alien_line=LINE-only).
2. `feat(storage)`: migration 007 (fully specified per must-fix #8) + `ReferenceRow`/`ReferenceKind` +
   kind-aware repo + `Migration007Tests` + coexistence tests.
3. `feat(reference)`: `OptimalReferenceBuilder` (pure) + `LapRepository.BestSectorsByTriple` +
   `LapParquetReader.ReadLap`; **per-sector outlier guard (#3)**; guards PB-exists / gain≥`MinOptimalGainMs` /
   idempotent; store as sector-boundary times only (#7); builder tests.
4. `feat(reference)`: persist optimal as idempotent `StartAsync` catch-up (NOT in stop-order, per baker fix) +
   registration test.
5. `feat(contracts,reference,coach)`: `optimal_gap_ms`/`sector_optimal_gap_ms` (numbers per reconciled map) +
   `ReferenceLookup.Get(kind)`; debrief computes deficit from live+persisted at session-end with first-session
   fallback (#2); **field-16 reconciliation (#4)**; Gold + `GoldFieldNames` + `CoachStartupValidator.SampleView`
   non-null + `DebriefTemplate` ranking line.

**PR-B2 (P3 coaching remainder):**
6. `docs(adr)`: ADR-0020 AggregatedLoss abs-then-average + sum-invariant + argmax cross-unit norm (M35/M36).
7. `refactor(reference)`: diagnostic diffs on `CornerContribution` (M35).
8. `feat(contracts,reference)`: AggregatedLoss 6-9 + accumulator + **falsifiable** sum-invariant + completeness
   probe with concrete channel set (M35).
9. `feat(contracts,reference)`: dominant channel 10-11, distinct picker, **`IOptions` cross-unit scales (#6)**
   + config-flips-the-pick test (M36).
10. `feat(coach)`: render channel+value in debrief, replaces `dominant_reason` (M36).
11. `feat(contracts→reference→coach)`: `CornerEvent brake_release_diff_m=21` (M33) + Gold lockstep note.
12. `feat(contracts→pipeline→reference→coach)`: `CornerEvent brake_lockup_score=22` (M33) + Gold lockstep.
13. `feat(contracts→reference→coach)`: `CornerEvent short_shift_score=23` (M33) + Gold lockstep.
14. `feat(contracts)`: `SessionEvent 19/20` + `AggregatedLoss 12` + `SectorCornerMembership`/
    `BalancePhaseTrend`/`LossTrend` (M41).
15. `feat(reference)`: per-phase balance→trend; per-corner loss trend; grounded sector→corner membership (M41).
16. `feat(coach)`: trend/membership/balance in Gold session payload + grounded `setup_hint` + Gold lockstep
    (M41).
17. `feat(llm)`: `RouteOptions.CacheSystemPrompt` default off (M39) — **likely deferred, owner decision**.

**PR-B3 (Ghost alien-LINE):**
18. `feat(reference)`: `ReferenceKind.AlienLine` + "alien_line" string; `ReferenceTriple` kind-suffixed
    filename helper; ADR-0021 addendum (alien_line LINE-only, ghost decode provisional).
19. `feat(tools)`: `SimCoach.GhostImport` — container/zlib decode + 130-byte record parse (world XZ +0/+8) +
    validate-or-fail + decode unit test vs **owner-produced/anonymized** specimen (#specimen).
20. `feat(tools)`: lap-split by loop closure + nearest-point align (~2 m deviation-ceiling guard) + per-metre
    resample → LINE-only `ResampledLap` with **seam validity mask (#1)** + alignment/suppression tests.
21. `feat(tools)`: `ReferenceParquetCodec.Write` + `ReferenceRepository.Upsert kind='alien_line'` with
    provenance.
22. `feat(reference)`: `ComputeSession.InitSession` prefers `alien_line` for `_lineReference` + **M38 gate
    re-tune + alien-regime RU phrasing (#5)** + line-only invariant test (alien never feeds `_reference`/TIME)
    + seam-suppression test + priority-tier test.

## Out of scope (explicit)

M40 (streaming debrief) — deferred to Phase-4/Voice-TTS. M46 live overlay optimal-delta — deferred
(debrief-only first). M46 per-corner stitching — deferred (per-sector decided; per-corner injects ±0.2–0.8 s
seam noise ≈ the signal). **Ghost SPEED/TIME entirely** — log-encoded clock yields untrustworthy speed
(vmax 400–570 vs ~285). **Ghost pedals as a coaching input** — decoded but single-car-verified; a future
line-anchored clock-free brake-point cue is a possible follow-up, not this set. Multi-ghost consensus-line —
out unless the owner picks the consensus path. Community ghost exchange, replay-focus-on-fastest-car UX,
iRacing/LMU/F1 ghost/optimal sources — all future (ghost decode stays ACC-specific inside the offline tool).

## Owner decision points

**RESOLVED (owner, 2026-07-15):**

3. **PR-B2 split → KEEP ONE PR-B2.** Honor the P3 ratification of a single PR-B. Guard rails required: enforce
   per-component Strict→Defender→Judge on each commit, and add an explicit **in-game acceptance checklist**
   asserting non-zero `dominant_channel`/trend values on a known replay (the diagnostic payload is the least
   observable surface, so it gets a concrete verification step). M39 stays inside this PR unless deferred (#4).
5. **Ghost source → SINGLE-GHOST + SEAM MASK.** One ghost → one `alien_line` (matches the reviewed importer,
   simplest code). The seam validity mask (critical must-fix #1) suppresses the noisy Parabolica/start-finish
   bins. Consensus-median is a documented future upgrade, not this ship.
2. **Metric reconciliation → SUPERSEDE FIELD 16.** Cross-session `optimal_gap_ms` becomes THE headline number;
   demote the within-session field-16 `theoretical_best_gap_ms` to a **first-session-only fallback** (shown
   only when no cross-session optimal exists yet). One clean number for the LLM. Still needs Gold-schema
   sign-off (#-gold below). Resolve jointly with must-fix #2/#4 in PR-B1 commit 5.
8. **Branch & decoder → LAND SMALL PR NOW + PORT DECODER.** Land the tooling/docs (`ShmProbe`,
   `AllowReplayCapture`, `GroundTruthDump` ext, research docs) from `feat/replay-telemetry-capture` as a small
   standalone PR; port the ghost decoder from scratchpad/workflow into `tools/SimCoach.GhostImport` as part of
   PR-B3. Gets the research merged and unblocks B3 (which cannot exist without the port).

**PROCEEDING WITH REVIEW-BACKED DEFAULTS (owner may override):**

1. **PROTO field allocation (M46 vs M41):** M46 takes `SessionEvent` **21/22**, leaving 19/20 for M41 (honors
   the ratified additive map). Second-to-merge rebases onto the agreed map; never reuse/renumber.
4. **M39 `CacheSystemPrompt`: DEFER** out of this PR set — pure metering-prep, "no P3 win", nothing in-game
   testable, model-dependent hardcoded threshold. (Also shrinks the kept-whole PR-B2.) If the owner keeps it:
   `IOptions` prefix minimum + a test asserting the flag toggles a `cache_control` marker.
6. **Ghost seam policy: FULL suppression** of pn 0.00–0.02 and 0.92–1.00 via the mask (accept no Parabolica
   coaching for now — those bins are artifact-contaminated).
7. **Ghost provisional-decode: ACCEPT** the fail-fast import guards (arithmetic + bbox + loop-closure + ~2 m
   deviation ceiling) as sufficient to ship Monza/BMW; **require re-validation before trusting a new
   car/track.**
9. **Ghost pedals: DEFER.** Brake/throttle decoded but single-car-verified — stay strictly line-only now; a
   line-anchored clock-free positional brake-point cue is a possible future follow-up.

**M46 recency/staleness → DEFER CONFIRMED (owner, 2026-07-15), on binding condition.** No sector-recency
window ships. Rationale: three in-plan mechanisms already close the stale-sector-best risk more cheaply than a
recency window, and the window carries its own failure mode:
- `weather_bucket` in the triple key already scopes out the biggest grip confound (wet/dry/temp) — sectors are
  stitched within one bucket.
- the **per-sector outlier guard (must-fix #3)** rejects a sector-best that sits > N robust-stddev (or
  `MaxSectorOutlierMs`) below that sector's clean-time distribution, catching the real poisoning modes
  (slipstream, undetected cut, grip spike) *independent of lap age* — i.e. the worst cases a recency window
  would catch are already caught.
- `sector_sources_json` provenance makes any suspicious target auditable.
- a recency window would add its own tuning trap: too short discards a legitimately-fast older lap (target
  artificially low), too long is inert — a knob with a real downside for a problem not yet observed (YAGNI).
- **Binding condition:** the defer is valid ONLY because the per-sector outlier guard (#3) ships. Therefore
  guard #3 is MANDATORY in PR-B1 commit 3 — if it were dropped, recency would have to be reconsidered.
- Revisit a recency window only if usage shows stale bests setting unreachable targets.

**Still needs explicit owner sign-off before PR-B1 commit 5:**
- **Gold-schema sign-off** — the new `optimal_*` aggregate fields leave the machine in Gold JSON. Consistent
  with the privacy rule (aggregates only), but schema additions need owner sign-off per convention.
