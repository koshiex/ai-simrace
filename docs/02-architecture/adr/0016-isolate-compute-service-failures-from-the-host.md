# ADR-0016: Isolate compute-service failures from the host (a bad lap must not stop recording)

**Status**: Accepted
**Date**: 2026-06-27

## Context

`ComputeService` is a `BackgroundService`, and the host never overrides
`HostOptions.BackgroundServiceExceptionBehavior`, so it uses the framework default **`StopHost`**.
Hosted services stop in reverse registration order, so when `ComputeService` threw mid-session (issue
#13: a duplicate `lap_number` insert), the host stopped and took `McapRecorderService` and
`SessionManager` down with it — the recording died mid-session, not just the compute.

[ADR-0015](0015-monotonic-session-local-lap-numbering.md) removes the specific crash by renumbering laps
so the UNIQUE collision can't happen. But the blast radius (one compute exception → whole session +
recording lost) is the deeper fault and is worth closing on its own: losing one lap row is acceptable;
losing the session and the on-disk recording is not.

## Decision

**Compute exceptions are contained inside `ComputeService`/`ComputeSession`; they never propagate out of
`ExecuteAsync`.** Two layers:

1. **Narrow primary** — `ComputeSession.HandleLap` wraps only the `_laps.Insert(...)` call in
   `try/catch`, logs a `Warning` (session + lap), and continues. This is at lap cadence (~1/90 s), so it
   cannot flood the log and never runs on partially-mutated session state.
2. **Rate-limited backstop** — `ComputeService.ExecuteAsync` wraps the per-frame `session.Accept(frame)`
   in `try/catch`, logging the **first** failure at `Error` and then a single aggregate count at stream
   end (never one log per frame at ~400 Hz), and continues. `OperationCanceledException` from the
   enumerator is **not** caught here — it still flows to the outer handler for graceful shutdown.

We **reject** setting `BackgroundServiceExceptionBehavior = Ignore`: it is host-wide and would change
failure semantics for every hosted service, not just compute.

## Why

- **Recording survives any compute fault**, not only the known one — a defense-in-depth net for future
  bugs in kernels, segmentation, or storage.
- **Convention-aligned (backstop)** — `coding-conventions.md` says try/catch belongs only at host-service
  loop boundaries, logged via Serilog, then continue; the per-frame `ExecuteAsync` catch sits exactly
  there. The narrow `HandleLap` insert wrap is a deliberate exception to that rule — a domain-level guard
  on the single statement that can throw on bad data — chosen over relocating it so corner/sector state
  stays intact.
- **No log floods, no corrupt-state processing** — the narrow Insert wrap carries the realistic failure
  path at lap cadence; the per-frame backstop is rate-limited and, once ADR-0015 lands, essentially never
  fires.

## Tradeoffs

- The catch handlers themselves never run on corrupt state, but the surrounding `HandleLap` does mutate
  session state (`_lapCount`, the running PB, the published `LapEvent`, and `ReferenceStore.MaybeUpdate`)
  **before** the guarded `_laps.Insert`. So a swallowed insert leaves the live `SessionEvent.LapCount`/PB
  and any reference candidate inconsistent with the DB. Low impact: the durable `sessions.lap_count` is
  derived from the `laps` row count (not `_lapCount`), and the per-frame backstop may likewise run on a
  partially-mutated session after a throw. Acceptable for a last-resort net that, post-renumbering, fires
  only on an unforeseen fault.
- Swallowing a lap-row insert means that lap is absent from `laps` **only** — `laps.parquet` is built by
  the independent replay path at finalize, so the lap is still present there. That DB-short-of-parquet gap
  is exactly what the finalize-time set canary (ADR-0015) surfaces rather than letting it pass silently.

## Consequences

- `ComputeSession.HandleLap` gains a `try/catch` around the lap insert; `ComputeService.ExecuteAsync`
  gains a rate-limited per-frame `try/catch` plus an end-of-stream aggregate-count warning.
- No change to host wiring or `HostOptions`.
- Relates to [ADR-0015](0015-monotonic-session-local-lap-numbering.md) (the root-cause fix) — together
  they close issue #13: the crash can't happen, and if some other compute fault does, the recording lives.
