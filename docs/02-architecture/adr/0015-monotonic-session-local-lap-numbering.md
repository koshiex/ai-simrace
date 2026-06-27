# ADR-0015: Monotonic session-local lap numbering (don't echo the sim's resettable counter)

**Status**: Accepted
**Date**: 2026-06-27

## Context

`laps` has `UNIQUE(session_id, lap_number)` and `LapSegmenter.Build` labelled each lap with the sim's
own counter (`frames[0].LapNumber`). ADR-0012 had already demoted `lap_number` to "only a label" for
boundary detection, but it was still written through verbatim.

A live Spa session (`20260627-124851-172`) crashed fatally (issue #13). When the driver pressed ESC →
returned to box → drove out again, ACC started a new out-lap and **re-issued a `lap_number` already
completed this session**. The second `laps` row violated `UNIQUE(session_id, lap_number)`,
`LapRepository.Insert` threw `SqliteException (19)`, `ComputeService` let it propagate, and the host's
default `BackgroundServiceExceptionBehavior = StopHost` tore the whole host down — compute **and** the
recorder — mid-session.

The wrinkle is the parquet path. `LapParquetWriter` re-segments the recorded MCAP with its **own**
`LapSegmenter`, and `laps.parquet` is keyed by `lap_number` (ADR-0013), with consumers recovering
validity via `lap_number → laps.is_clean`. The live compute path (→ `laps` rows) and the replay path
(→ `laps.parquet`) read **independent** `DropOldest` channels (`McapRecorderService` subscribes
`"recorder"`, `ComputeService` subscribes `"compute"`), so any renumbering must be computed identically
on both streams or the join silently desyncs.

## Decision

**`LapSegmenter` assigns a session-local, monotonic `lap_number` via a per-stint offset, and the label
is threaded explicitly into the parquet** (`PositionResampler.Resample(..., lapNumber)`) so both paths
carry the same number.

```
intrinsic = frames[0].LapNumber
natural   = intrinsic + _lapOffset
if (_lastAssigned is not null && natural <= _lastAssigned):   // counter reset OR repeated-equal
    _lapOffset = _lastAssigned + 1 - intrinsic                // re-base: assigned == _lastAssigned + 1
    natural    = intrinsic + _lapOffset
assigned      = natural
_lastAssigned = assigned
```

- The first emitted lap inherits the sim's value as its base; thereafter the offset re-bases only when
  the offset-adjusted number fails to advance.
- The `<=` test is load-bearing: a **repeated-equal** counter collides on the UNIQUE constraint exactly
  like a decrease, so both must trigger a re-base.

## Why

- **Offset, not a running count.** Within a stint `assigned = intrinsic + constant`, so the label stays
  tied to the per-frame counter and is robust to dropped frames — exactly as drop-proof as the raw
  counter was. A *count*-based ordinal would desync the two independent streams after any single dropped
  frame and would latch forever on a spurious high counter value.
- **Correct on real hardware.** A pit return no longer crashes; the laps continue `…2, 3, [box] 4, 5…`
  instead of repeating and colliding.
- **Join preserved.** Both the live and replay `LapSegmenter`s run the same rule over the same per-frame
  counter, so `laps.parquet` row-group `lap_number`s match the `laps` rows and the ADR-0013
  `lap_number → laps.is_clean` join stays 1:1.
- **Unchanged on normal sessions.** A strictly-increasing counter never re-bases, so the label equals
  the intrinsic counter exactly — the 471 synthetic tests and the ADR-0012 desync golden are byte-stable.

## Tradeoffs

- **No relabel is perfectly drop-proof once the sim counter resets.** If back-pressure drops the exact
  pre-reset boundary lap on one stream only, the two offsets differ for the post-reset laps. The window
  is tiny (pit reset **and** >4096-frame consumer lag **and** the drop landing on the boundary lap), and
  it is made observable by a finalize-time DB↔parquet count canary in `SessionManager` (warns when the
  `laps` row count ≠ parquet bounded-lap count). The status quo for this scenario was a full-session
  crash, so any surviving relabel is strictly better.
- **`source_lap_number` becomes an ordinal.** `ReferenceStore` writes `completed.LapNumber` as
  `references.source_lap_number` (and into the reference parquet), so it is now the session-local label,
  not the sim counter. No consumer compares it to a live sim counter.
- **`sessions.lap_count` is unaffected** — `SessionManager.Finalize` derives it from the `laps` row
  count, not from `max(lap_number)`, so renumbering does not distort it.
- Keeping intrinsic numbering + a composite key / stint column would be drop-proof but cannot deliver
  the continuous `0,1,2,3,4,5` the user asked for, and would change the ADR-0013 join contract more than
  this relabel does.

## Consequences

- `LapSegmenter` gains `_lapOffset` / `_lastAssigned` and `AssignLapNumber`; `Build` is now an instance
  method. `PositionResampler.Resample` takes an explicit `lapNumber` (the hidden `lapFrames[0].LapNumber`
  read is removed); callers `ComputeSession.ResampleSelf` and `LapParquetWriter` pass
  `completed.LapNumber`.
- Compute-error isolation that stops the crash from killing the recorder is covered separately in
  [ADR-0016](0016-isolate-compute-service-failures-from-the-host.md).
- Tests: `LapSegmenterTests` (reset, repeated-equal, strictly-increasing regression); `ComputeSessionTests`
  and `LapParquetWriterTests` pit-return cases; a wired `Phase2ComputeE2EGoldenTests` reset case asserting
  the DB↔parquet `lap_number` sets are identical.
- Relates to [ADR-0012](0012-lap-boundary-from-position-wrap.md) and
  [ADR-0013](0013-clamp-non-monotonic-laps-in-parquet.md) — all three came out of real-hardware sessions.
