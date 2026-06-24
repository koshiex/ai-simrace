# ACC lap-boundary timing — `completedLaps` increments ~1 frame before `normalizedCarPosition` wraps

This card records why the first real ACC session segmented to **zero laps** even though lap/sector
compute was correct — a timing skew between two ACC shared-memory fields that synthetic fixtures never
reproduce, so it was invisible until live hardware.

## Symptom (real capture)

Monza, BMW M4 GT3, recording `20260624-174042-547`, 263 437 frames, ~400 Hz. Driver ran an out-lap +
3 timed laps; the session finalized **`0 lap(s), 0 clean`**: empty `laps`/`references` tables,
`references/` dir empty, `laps.parquet` only 440 B (schema, no row groups). Compute had started fine
(`model Dataset (7 corners)`) and recording was clean (11 segments, 0 drops).

## Root cause

`LapSegmenter` required a `lap_number` increment **AND** a `normalized_car_position` decrease on the
**same** frame transition. On live ACC the two are not simultaneous — `graphics.completedLaps`
increments roughly one frame **before** `normalizedCarPosition` wraps, and position is **pinned at
`1.0`** on the increment frame:

```
frame t   : lap 1, pos 1.0
frame t+1 : lap 2, pos 1.0     ← counter bumps, pos still 1.0   → "pos<prev" FALSE
frame t+2 : lap 2, pos 0.0     ← pos wraps, counter already 2   → "lap>prev" FALSE
```

So the conjunction is satisfied on **neither** transition. Over the whole session: 4 lap increments,
5 position wraps, **0** frames with both → 0 bounded laps. The synthetic builder flips both fields on
one frame, so all 471 offline tests passed. Additionally, the **out-lap → lap-1** crossing wraps
position with **no** `completedLaps` increment at all (it stays 0), so a counter-gated trigger also
silently drops the driver's first flying lap.

Cross-checks on the same capture (used to size the fix): the only negative `norm` deltas in 263 437
frames were 5 clean `1.0→0.0` wraps + 2 `-0.0000` float-noise, **zero** mid-lap drops, continuous
across all 11 segment joins; all 5 wraps at 222–228 km/h on the main straight; **0 of 9 263** sub-5
km/h box frames produced a wrap.

## Fix

Detect the crossing from the **position wrap alone** — `previous.NormalizedCarPosition > 0.9 &&
current.NormalizedCarPosition < 0.3` — and drop `lap_number` from the trigger (keep it as the lap
label only). Position is monotonic within a lap, and the high/low band self-debounces: a second
crossing needs the previous frame back above `0.9`, which only happens after ~a full lap, so a
pit/teleport reset (drops from mid-lap) cannot mint a phantom lap. See ADR-0012.

## Implementation notes

- The crossing predicate was **duplicated** in `ComputeSession` (to re-arm corner trackers each lap).
  Both were broken; the fix unifies them — `LapSegmenter.CrossedThisFrame` is the single source and
  `ComputeSession` reads it, so lap-close and corner-reset boundaries cannot drift.
- Mid-lap position resets (pit/teleport/dropped chunk) are counted as
  `LapSegmenter.SuspiciousResetsIgnored` and logged once at `Complete()` — a canary, since a clean
  live session has zero.
- `is_clean` already shields the reference store, so a mis-counted pit lap can never corrupt a PB.
- ACC `AccG` axes confirmed on this capture (relevant to corner work): `|gz|` (longitudinal) peaks
  ~2.0 g under braking, `|gx|` (lateral) peaks in corners — matches `telemetry.proto`'s
  `x = lateral, z = longitudinal`, units are g.
- Parsing MCAP offline: each message is a protobuf `TelemetryFrame`; `lap_number` is field 6 (varint),
  `normalized_car_position` field 8 (float32), `g_force_g` field 23 (sub-message, `x` = field 1).
