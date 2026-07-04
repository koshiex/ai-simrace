# Why compute emitted factually-wrong coaching numbers (and the fixes)

Read this before debugging a corner/sector `delta_ms`, min-speed, or debrief loss that looks wrong.
Four independent root causes made the detection layer emit false numbers **even on a clean personal-best
lap**. All are algorithmic (the LLM is only a selector+phraser — it cannot know a number is nonsense).

## 1. The reference is overwritten in place — on a PB lap every reference-relative diff is ~0

`ComputeSession` seeds its comparison reference from the lap being processed and, when that lap is a new
best, reassigns `_reference = self` (via the reference store's `MaybeUpdate`) inside `HandleLap`. So while
processing a PB lap the reference *becomes* the same lap, and every reference-relative quantity collapses
to **~0 (self == ref)**.

Consequences:
- Any pre-overwrite quantity (notably the **lap deficit** the plausibility guard compares against) MUST be
  captured *before* `MaybeUpdate` runs. `delta_ms` is measured against the pre-update reference; the guard's
  `_bestLapDeficitMs` is captured in the same window, above the `_reference = self` line. A refactor that
  moves the capture below `MaybeUpdate` silently reads ~0 and the guard stops working — pin it with a test.
- The on-disk reference is mutated in place (no snapshot/versioning — see master-backlog M37), so a fixture's
  reference can already equal its PB lap. The ground-truth gate therefore seeds a reference from the flying
  lap and asserts **both** the ~0 collapse **and** that the fixed number is far from the buggy value an
  independent oracle measures (the discriminator that fails loudly if a fix regresses).

## 2. Corner self-window span-mismatch — the loss was literally the reference's traversal time

The corner tracker used to *fire at throttle-resume* (first frame past the speed minimum with throttle above
a threshold). On flat/transit/exit-light corners that point sits ~2 frames past the start, so the **self**
window collapsed to ~2 frames while the **reference** time-at-position was measured over the full geometric
`[Start, End]`. `delta_ms` = self-traversal − ref-traversal then reported the reference's whole traversal
time as a "loss" — e.g. a bogus **3929 ms at Curva Grande** (= the reference's time through that corner).

Fix: fire at the geometric corner **End** and run all *self* kernels over the full `[Start, End]` span,
scoped to a **single lap-crossing** (a dirty/pit/spin lap crosses a span multiple times — never concatenate
crossings). See `CornerEventBuilder` (`FramesInSpan`, the self-degenerate guard) and `CornerTracker`.
Watch the M16 interaction: the brake-onset window is deliberately widened *upstream* of Start, so the
self delta/duration path must NOT fall back to that widened buffer (it would re-introduce a span mismatch).

Min-speed has the same trap: a corner whose `[Start, End]` contains **no genuine interior speed minimum**
(flat corners; chicane *entries* whose real apex lives in the paired downstream element) yields a boundary
reading that jitters lap-to-lap. The kernel computes `HasInSpanMinimum` and **suppresses** the min-speed
advice when false; the chicane's min-speed is carried by the downstream element whose span holds the apex.

## 3. Out/in-lap poisoning — a frame-level poison latch, not a whole-lap filter

Emitting corners/sectors and accumulating session/sector means on non-coachable frames (pit / invalid /
before the lap has started) poisons everything downstream: an out-lap's ~66 s S1 dragged into a mean can
flip the sign, so the debrief announces a **"−14.8 s gain in S1" on the driver's best S1 of the day**.

Fix: a **frame-level poison latch** in `ComputeSession` — on the first non-coachable frame, suppress all
further corner/sector emits *and* mid-lap accumulation for that lap (`_prevSectorCrossPos` keeps advancing
so the next coachable crossing still measures from the right position); re-arm on lap reset. The shared
`CoachableFramePredicate.IsCoachable` (= `is_valid_lap && !is_in_pit_lane`) also feeds the fuel gate. The
**whole-lap** `CleanLapPredicate` is a *separate* thing used only for reference-seeding — do not conflate.

## 4. Sector-delta aggregation — median, not mean

Mean-of-crossings for per-sector delta is poisonable by any surviving out/in-lap sample. Attribution uses
the **median** of clean laps (`SectorDeltaAggregator`). Keep it separate from best-sector highlighting
(`_bestSectorMs = min`) — the "purple/best sector" UI must compare against the previous *best*, not the
median, or it will mislabel sectors.

## Regression net

`docs/05-implementation/ground-truth-revalidation.md` is the run-book: a throwaway
`tools/SimCoach.GroundTruthDump` decodes a recorded session via the real `McapSegmentEnumerator`, an
independent `scripts/groundtruth_oracle.py` computes a truth-oracle, and an **env-gated** xUnit
(`GroundTruthRevalidationTests`, skips without `SIMCOACH_GROUNDTRUTH_FIXTURE`) asserts the emitted numbers
match within coaching tolerance. Raw MCAP / CSV / `truth.json` stay on disk (privacy) — never committed.
