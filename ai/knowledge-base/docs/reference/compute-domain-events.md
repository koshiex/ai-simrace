# Compute service + domain events (Phase 2 PR-E)

`ComputeService` (in `SimCoach.Reference`, **not** `SimCoach.Pipeline` — Pipeline references only
Contracts and can't see the resampler/`LapRepository`/`TrackModelStore`) turns the live telemetry
fan-out into the four domain events via a stateful `ComputeSession` worker, published on a
`DomainEventFanOut`.

## Finalize must run on StopAsync, not in the ExecuteAsync finally

The load-bearing correctness point. Every fan-out subscriber's `await foreach` ends *together* when
`IngestService` calls `fanOut.Complete()`, so a `finally` block at end-of-stream runs **concurrently**
across `SessionManager`, the recorder, and `ComputeService` — the host's reverse-registration stop
order does **not** gate those finally blocks. If `SessionManager` finalized counts/PB from the `laps`
table in its `ExecuteAsync` finally, it would race `ComputeService` still draining and writing lap rows.

Fix: `SessionManager` finalizes in an overridden `StopAsync` (after `base.StopAsync`). The host stops
services in reverse registration order and **awaits each** — registering `SessionManager` first means
it stops last, after `ComputeService.StopAsync` has fully drained. Registration order in
`TelemetryComposition`: `SessionManager, McapRecorderService, ComputeService, IngestService`. The
`laps.parquet` conversion also lives in `SessionManager.StopAsync` (recorder has flushed its segments
by then). Tests must call `StopAsync` before asserting finalized state.

## DomainEventFanOut is lossless (diverges from TelemetryFanOut)

`TelemetryFanOut` uses bounded drop-oldest channels (333 Hz, latest-wins). `DomainEventFanOut` uses
**unbounded** channels: domain events are sparse and a dropped `SessionEvent` is unacceptable. Same
immutable-snapshot/lock-free-publish shape otherwise.

## Reference deltas come from the grid, no second resample

Reference sector/corner times are derived from the reference `ResampledLap` grid, not stored
separately: sim sector boundaries are fixed track positions, so the live sector-cross
`normalized_car_position` maps to a grid index (`round(pos·lapLengthM)`) and `TMsFromLapStart` is
summed across the range. Corner ref metrics reconstruct minimal frames over the grid slice
(`GridMetrics.SliceToFrames`) and re-run the C4 kernels — keeping self/ref strictly comparable.
Racing-line deviation matches each raw self frame to the ref grid by interpolating world X/Z at the
frame's position (no mid-lap self resample needed; CornerEvents fire mid-lap at corner-exit).

## Corner trackers reset on every crossing, not just completed laps

`CornerTracker` fires once per corner per lap. The first start-line crossing completes no lap (its
start was never observed, so `LapSegmenter` discards it), so resetting only on `CompletedLap` would
leave trackers latched forever. `ComputeSession` detects the crossing itself (lap-number increment +
position wrap) and resets trackers/sector state on **every** crossing.

## Single parquet schema for laps and references

`ResampledLapParquet` holds the one 17-column schema + per-row-group read/write; both
`LapParquetWriter` (multi-lap `laps.parquet`) and `ReferenceParquetCodec` (single-lap reference file)
use it so writer and reader can't drift. ParquetSharp reads by **indexed** `rowGroup.Column(i)` —
there is no `NextColumn()` on the reader side (writer-only).

## ITrackLengthProvider lives in Storage

Moved from `SimCoach.Reference` to `SimCoach.Storage` so `SessionManager` (Storage) can use it for the
`laps.parquet` conversion without a Reference→Storage cycle. The ACC bridge (`AccTrackLengthProvider`)
stays in `App` (the only project allowed to reference the ACC adapter).
