# ADR-0012: Lap-boundary detection from the position wrap, not the lap counter

**Status**: Accepted
**Date**: 2026-06-24

## Context

`LapSegmenter` closed a lap only when a frame showed **both** a `lap_number` increment **and** a
`normalized_car_position` decrease on the *same* frame transition (PR-E, C3). It passed 471 synthetic
tests because `SyntheticSessionBuilder` flips both fields on the same frame.

The first real ACC capture (Monza, BMW M4 GT3, recording `20260624-174042-547`, 263 437 frames)
finalized **`0 lap(s), 0 clean`** despite three timed laps — empty `laps`/`references`, schema-only
`laps.parquet`. Parsing the MCAP showed why: ACC's `completedLaps` increments **~1 frame before**
`normalizedCarPosition` wraps, and position is **pinned at `1.0`** on the increment frame:

| transition | lap_number | norm | old AND-predicate |
|---|---|---|---|
| counter bump | 1 → 2 | 1.0 → 1.0 | `norm < prev` false |
| position wrap | 2 → 2 | 1.0 → 0.0 | `lap > prev` false |

Across the session the conjunction held **0 times** (4 lap increments, 5 position wraps, 0 same-frame).
The same duplicated predicate lived in `ComputeSession` to re-arm the corner trackers per lap, so it
was broken in two places. Separately, the **out-lap → lap-1** crossing wraps position with **no**
counter increment at all, so any counter-gated scheme also drops the driver's first flying lap.

## Decision

**A start-line crossing is a high→low wrap of `normalized_car_position`
(`previous > 0.9 && current < 0.3`). `lap_number` is no longer part of the trigger — it remains only
the lap label.** The crossing verdict is computed once in `LapSegmenter` and exposed
(`CrossedThisFrame`); `ComputeSession` reads it instead of re-deriving the predicate, so the
definition lives in exactly one place.

- Position is monotonic within a lap, so only the start line produces a high→low step.
- The high/low band is **self-debouncing**: a second crossing needs the previous frame back above
  `0.9`, which only happens after nearly a full lap — so a pit/teleport reset (which drops position
  from mid-lap, previous frame below `0.9`) cannot mint a phantom lap. Such resets are counted as
  `SuspiciousResetsIgnored` and logged once at session end.

## Why

- **Correct on real hardware**: recovers all bounded laps including the out-lap → lap-1 boundary, so
  the driver's fastest (first) flying lap is retained as a PB candidate.
- **No counter dependence**: ACC's counter/position desync — and its absence on the out-lap — no
  longer matters.
- **One source of truth**: the duplicated `ComputeSession.IsStartLineCrossing` is deleted, so the
  lap-close and corner-reset boundaries can never drift apart.
- **Reference store stays protected**: only `is_clean` laps enter the reference store, so even a
  mis-counted pit lap cannot corrupt a PB/reference.

## Tradeoffs

- Dropping the `lap_number` conjunct removes a guard against a bare position wrap. The residual risk
  is a non-LIVE teleport/pit reset; it is mitigated by the `> 0.9` high-water-mark (a reset drops from
  mid-lap) and by `AccFrameMapper.IsRecordable` already filtering non-LIVE frames. The real capture
  had exactly 7 negative `norm` deltas — 5 clean `1.0→0.0` wraps and 2 float-noise — and **zero**
  mid-lap drops, across all 11 segment joins.
- The `0.9 / 0.3` thresholds are fixed constants; a sim that reports a coarser position grid could in
  principle need tuning. The warn-on-suspicious-reset canary surfaces that instead of silently
  minting laps.

## Consequences

- `LapSegmenter` gains `CrossedThisFrame` + `SuspiciousResetsIgnored`; `ComputeSession` drops its
  duplicate predicate and `_previousFrame`, resets on `CrossedThisFrame`, and warns on suspicious
  resets at `Complete()`.
- Regression coverage: a unit test reproducing the ACC desync (counter a frame early at `pos 1.0`),
  a mid-lap-reset test asserting no phantom lap, and the Phase-2 E2E golden parameterized to replay
  the desync ordering. `SyntheticSessionBuilder` is unchanged (its same-frame wrap still crosses).
- Knowledge: KB `acc-lap-boundary-timing.md`.
