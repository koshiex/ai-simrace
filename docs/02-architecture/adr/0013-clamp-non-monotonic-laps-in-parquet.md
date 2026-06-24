# ADR-0013: Clamp non-monotonic crash laps into laps.parquet (don't drop them)

**Status**: Accepted
**Date**: 2026-06-24

## Context

`laps.parquet` stores each bounded lap resampled onto a 1-metre position grid (time-at-position,
world coords) for overlays and post-session review. `PositionResampler` requires
`normalized_car_position` to be monotonically non-decreasing — it walks the grid forward assuming the
car only moves forward along the lap.

A real ACC session surfaced the problem (Monza, session `20260624-193240-243`): the driver crashed
into a wall, the car was thrown backward, and `normalizedCarPosition` stepped *down* for a few frames
(`0.3796 < 0.3797`). The resampler threw `ArgumentException`, and because `LapParquetWriter` let one
throw abort the whole conversion, the session finalized with `parquet_path = NULL` despite four
perfectly resampleable laps. Note the asymmetry the driver observed: a *wall crash* (car rolls
backward → position decreases) tripped the guard, but a *stall* (car frozen → position constant, not
decreasing) did not — the guard rejects a strict backstep only.

A crash lap is never a reference (it is `is_clean = 0` — track limits), but its non-crash sectors are
still worth reviewing and coaching, so dropping the whole lap from the parquet loses useful data.

## Decision

**For `laps.parquet`, clamp a backward position step to the running max instead of rejecting the lap;
keep the strict (throwing) behaviour everywhere a reference is built.**

- `PositionResampler.Resample` takes `clampNonMonotonic` (default `false`). When `true`, a frame whose
  position dips below the running max is pinned to that max: `pos[i] = max(raw, pos[i-1])`.
- `LapParquetWriter` passes `clampNonMonotonic: true` — every bounded lap, including a crash/spin,
  lands in the parquet.
- `ComputeSession.ResampleSelf` (the reference candidate) keeps the **strict** default, so a
  crash lap still cannot become a reference (belt-and-suspenders with the `is_clean` gate).
- `LapParquetWriter` keeps a per-lap skip only as a safety net for genuinely degenerate laps
  (e.g. < 2 frames), so one such lap can never abort the whole file.

### What the clamp looks like in the data — read this before puzzling over a flat spot

The clamped (crash) stretch shows up as a **flat run of identical `position_normalized`** values: while
the car was thrown backward / spinning, the grid position is held at the pre-crash maximum and only
*time* advances. So in an overlay a crash lap has a short plateau where position stops climbing — that
plateau **is** the crash, not a bug, not corrupt data. The rest of the lap before and after the plateau
is resampled exactly. The lap is always `is_clean = 0`.

## Why

- **Keeps coachable data**: the crash lap's clean sectors stay reviewable in the parquet/overlay; live
  corner/sector/lap events already coach the rest of the lap (they are built from raw frames,
  independent of resampling), and this makes the same lap available for post-session analysis too.
- **References stay protected**: the strict path + `is_clean` gate mean a non-monotonic lap can never
  become a PB/reference, so the clamp distortion never pollutes the benchmark.
- **One bad lap never nulls the file**: the original failure (whole `laps.parquet` lost to one crash)
  cannot recur.

## Tradeoffs

- The clamped plateau is an artifact: time-at-position around the crash is compressed onto one grid
  cell, so any metric read from that stretch is meaningless. Acceptable — that stretch is off-track
  crash telemetry with no coaching value, and the lap is flagged `is_clean = 0`.
- A bounded lap that genuinely detours through the pit lane mid-lap would also be clamped rather than
  rejected in the parquet. Rare, and harmless: it is `is_clean = 0` and never a reference; the parquet
  row is review-only.
- `laps.parquet` row-group count can therefore differ from a naive "monotonic laps only" expectation —
  it now matches the bounded-lap count (minus only degenerate laps).

## Consequences

- `PositionResampler.Resample(frames, lapLengthM, clampNonMonotonic)`; `LapParquetWriter.Write` returns
  the count of (now only degenerate) skipped laps, logged by `SessionManager`.
- Tests: `PositionResamplerTests` clamp unit; `LapParquetWriterTests` asserts a crash lap lands in the
  parquet (row group present, zero skipped) rather than being dropped.
- Relates to [ADR-0012](0012-lap-boundary-from-position-wrap.md) (live-ACC lap detection) — both came
  out of the first real-hardware session.
