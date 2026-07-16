# ADR-0020: AggregatedLoss — abs-then-average diagnostic diffs, a falsifiable sum-invariant, and a cross-unit argmax domain

**Status**: Accepted
**Date**: 2026-07-16

## Context

`AggregatedLoss` (proto:201) rolls a driver's per-corner losses up across a session — one row per
corner, bounded top-N by `total_loss_ms`. Today it carries only the magnitude fields (`total_loss_ms`,
`avg_loss_ms`, `sample_count`) plus `dominant_reason` (field 5), a string copied up from
`CornerEvent.reason`. That per-corner `reason` is produced by `ChooseReason`
(`CornerEventBuilder.cs:252–275`), a *deliberately rough* heuristic that argmaxes across mixed units
(metres for brake/throttle, km/h for speed) with no normalization — its own doc-comment calls it
"approximate".

M35 adds four per-channel **diagnostic diffs** to `AggregatedLoss` (the session-level average of each
corner-diff channel) and M36 replaces the rough per-corner `reason` roll-up with a normalized,
config-scaled **dominant-channel** argmax at the session level. Two design questions must be pinned
*before* code so that commit 8's completeness probe and commit 9's argmax test key off a fixed
channel set rather than drifting:

1. **How is a per-channel diff aggregated across corners** — average-then-abs or abs-then-average —
   and what invariant makes that choice testable rather than a comment?
2. **Which channels does the M36 dominant-channel argmax range over**, given that one of the four
   diagnostic channels (`racing_line_deviation_m`, field 9) is an *unsigned RMS*?

The per-corner diff channels this ADR governs are the `CornerEvent` fields:

| Channel | `CornerEvent` field | Sign semantics |
|---|---|---|
| `brake_point` | `brake_point_diff_m` (4) | **signed**, bidirectional (− = braked earlier, + = later) |
| `throttle_resume` | `throttle_resume_diff_m` (8) | **signed**, bidirectional (− = resumed later) |
| `min_speed` | `min_speed_diff_kmh` (5) | **signed**, effectively same-sign when lossy (− = slower) |
| `line_deviation` | `racing_line_deviation_m` (9) | **unsigned RMS**, always `>= 0` (`CornerEventBuilder.cs:220–244`) |

## Decision

### 1. Aggregation is abs-then-average: `aggregate_channel == mean(|per_corner_diff|)`

Each session-level diagnostic diff (`AggregatedLoss` fields 6–9) is the **mean of the absolute
per-corner diffs** over the conditioned corner set, not the absolute value of the mean:

```
aggregate_channel = mean( |per_corner_diff| )     // abs-then-average — CHOSEN
                  ≠ | mean( per_corner_diff ) |     // average-then-abs — REJECTED
```

Average-then-abs lets a corner where the driver braked 5 m early cancel a corner where they braked 5 m
late, reporting ~0 "brake-point error" for a driver who is wrong on every corner. Abs-then-average
reports the true typical magnitude of the mistake, which is what a coaching diff is for.

This is a genuine, falsifiable invariant only on a **bidirectional** channel. On a same-sign channel
(`min_speed`, negative whenever the driver is slower) every per-corner diff shares a sign, so
`mean(|x|) == |mean(x)|` identically — a sign swap yields the same number and no test can distinguish
the two aggregation orders. The **sum-invariant / sign-fault test (commit 8) must therefore target a
bidirectional channel** — `brake_point` or `throttle_resume` — on a fixture that contains at least one
positive and one negative per-corner diff, and must assert those mixed signs are present (fail-fast)
before asserting the invariant, so the test cannot silently pass on a degenerate all-same-sign fixture.

### 2. The four diagnostic diffs (fields 6–9) are report-only

All four channels — including `line_deviation` — are aggregated abs-then-average and surfaced as
**report-only** diagnostic context. They describe *what* the driver is doing differently on average;
they are not summed into any loss total and carry no authority over the dominant-channel decision.

### 3. The M36 dominant-channel argmax domain is the 3 SIGNED channels only

The dominant-channel argmax ranges over **`brake_point`, `throttle_resume`, `min_speed`** — the three
**signed** loss channels — and **excludes `line_deviation`**. This matches the domain of the existing
`ChooseReason` heuristic (`CornerEventBuilder.cs:252–275`), which never considers line deviation.

`racing_line_deviation_m` is an unsigned RMS (`CornerEventBuilder.cs:220–244`): it is `>= 0` on every
corner by construction, including corners the driver took *well*. Feeding it into a magnitude argmax
with any positive cross-unit scale makes it win on nearly every corner regardless of true time loss —
an **unfalsifiable** `dominant_channel`, because there is no input under which it *doesn't* dominate.
Excluding it from the argmax domain keeps the pick falsifiable: flipping which signed channel is
largest must change the observed `dominant_channel`.

The exclusion is on the **argmax domain only**. `line_deviation` may remain a report-only diagnostic
diff (field 9). If corner-line shape must ever become a dominant-channel candidate, the argmax must use
the **signed** phase deviations (`CornerEvent` 18–20, ADR-0018), never the unsigned RMS field 9.

### 4. Cross-unit scales are `IOptions` weights, not magic numbers

The three signed channels are in different units (metres, metres, km/h). The argmax compares them after
multiplying each by a per-channel scale drawn from `ComputeOptions` (`IOptions<T>`). These scales are
**decision-driving weights** — they set the exchange rate between "1 m of brake-point error" and
"1 km/h of min-speed error" and therefore change which channel is called dominant. They are config, not
inlined constants, precisely so the pick is tunable and testable: **flipping a scale in config must be
able to change the observed `dominant_channel`** on a fixture where two channels are close.

### 5. `dominant_channel_value` is a ranking magnitude, never an additive time

`dominant_channel_value` (`AggregatedLoss` field 11) is the **scaled ranking magnitude** of the winning
channel — the value the argmax maximized, in the cross-unit scaled space. It is a comparison key, **not
a millisecond quantity**, and must **never be summed with `total_loss_ms`** or presented as "the corner
cost X ms". The authoritative time loss is `total_loss_ms` / `delta_ms`, computed unit-correctly and
separately. The debrief either omits `dominant_channel_value` and surfaces `dominant_channel` alongside
the corner's real `delta_ms`, or names/labels the value so it plainly reads as a heuristic ranking score
rather than a time.

### 6. Diagnostic-diff averages are conditioned on the `DeltaMs > 0` lossy-corner set

The diagnostic-diff averages (fields 6–9) are taken over the **same lossy-corner set as
`aggregated_losses`** — corners where `DeltaMs > 0` — a single gate shared with the roll-up, not a
second independent filter. Averaging channel diffs over corners the driver took at or above reference
would dilute the diagnostic with corners that have no loss to explain. One gate, one corner set, so the
diffs describe the same corners the losses are rolled up from.

## Why

- **Truthful magnitude.** Abs-then-average is the only aggregation that reports a wrong-on-every-corner
  driver as wrong; average-then-abs hides consistent one-directional error via cancellation.
- **Falsifiable by construction.** Pinning the invariant to a bidirectional channel with asserted mixed
  signs, and excluding the always-positive RMS from the argmax domain, gives both the sum-invariant and
  the dominant-channel a test that can actually fail — the whole point of the ground-truth discipline.
- **Additive and non-breaking.** `dominant_reason` (field 5) is **retained** (the additive-only contract
  forbids reuse/removal, ADR-0006/AGENTS.md); M36 stops treating it as authoritative and drives coaching
  from `dominant_channel` (10) + `dominant_channel_value` (11) instead. Fields 6–11 are new numbers only.
- **Tunable, not magic.** The cross-unit scales live in `ComputeOptions` so the exchange rate between
  channels is an explicit, reviewable, testable decision rather than a buried literal.

## Consequences

- `telemetry.proto` `AggregatedLoss` gains fields 6–9 (diagnostic diffs, M35/commit 8) and 10–11
  (`dominant_channel`, `dominant_channel_value`, M36/commit 9). Field 5 (`dominant_reason`) stays,
  populated for back-compat but no longer authoritative.
- The per-channel accumulator aggregates abs-then-average over the `DeltaMs > 0` lossy-corner set;
  commit 8's completeness probe asserts the **concrete** channel set above (not a count).
- The commit 8 sign-fault test targets `brake_point` or `throttle_resume` on a mixed-sign fixture and
  fail-fast asserts the mixed signs before asserting `aggregate == mean(|diff|)`.
- The commit 9 argmax ranges over the 3 signed channels only; a test asserts `line_deviation` is not
  picked when a signed loss exists, and a second test asserts flipping a `ComputeOptions` scale changes
  the observed `dominant_channel` on a near-tie fixture.
- The debrief never sums `dominant_channel_value` into a time total (commit 10 render decision).
- The diffs 6–9 are report-only diagnostics; whether they surface in Gold is decided in commit 10, not
  by this ADR.
