# ADR-0011: Single-owner session identity; recordings layout; no physical MCAP concat

**Status**: Accepted
**Date**: 2026-06-21

## Context

Phase 2 introduces SQLite persistence. The `sessions` row needs `mcap_path` (`NOT NULL`) plus
`track_id`/`car_id`/`weather_bucket`, and `laps` rows carry a `session_id` foreign key. Three
seams surfaced during the Phase-2 plan review:

1. **Two owners of session identity.** `McapRecorderService` mints its own `sessionId`
   (`yyyyMMdd-HHmmss-fff`) privately inside `ExecuteAsync` and writes
   `recordings/<sessionId>/segment-NNNN.mcap`. If `ComputeService` independently minted another
   id to write the `sessions` row, the row's `mcap_path` would point at a different directory than
   the recorder actually used.
2. **Single-file assumption.** The plan said "convert the session's MCAP", but a session is a
   *directory of rotating 60 s segments* (`segment-0000.mcap`, `segment-0001.mcap`, …). There is
   no single MCAP file, and `data-model.md` described a fictional concatenated `raw.mcap`.
3. **Path drift.** `data-model.md` documented `%LOCALAPPDATA%/SimCoach/sessions/<ts>/raw.mcap`,
   but the shipped `RecordingOptions.BasePath` is `…/SimCoach/recordings` with `segment-NNNN.mcap`
   and no `raw.mcap`.

## Decision

### Session identity is allocated by the producer, before any frame is published

The key ordering rule: **identity exists before frame #1 reaches any consumer**, so no consumer ever
races another to discover it.

- **`IngestService` (the producer) allocates `SessionId`** (`yyyyMMdd-HHmmss-fff`) when it starts
  pulling from the `ITelemetrySource`, *before* publishing the first frame, and resolves the shared
  `SessionContext` synchronously at that point. The `Ready` `TaskCompletionSource` uses
  `TaskCreationOptions.RunContinuationsAsynchronously` so consumer continuations never run inline on
  the publish thread. Because `Ready` is resolved before `Publish(frame #1)`, every fan-out
  subscriber sees identity already available — **the inter-subscriber race is structurally removed**,
  not merely narrowed. The ms-suffix on `SessionId` preserves the recorder's original invariant: a
  crash + restart within one second must not reuse (and truncate) the previous session's directory.
- `IngestService` (Pipeline) owns only the opaque `SessionId`/start instant — **not** storage paths
  (no layering leak). `SessionContext = { SessionId, StartedAtUtc }`.
- **`SessionManager` (`Storage`, `BackgroundService`) owns the `sessions` row and the session
  directory.** On `SessionContext.Ready` it derives `SessionDirectory = <BasePath>/<SessionId>` and
  **creates the directory** (the identity/path owner creates it — so `mcap_path` always points at a
  real directory regardless of recorder timing). `McapRecorderService` and `ComputeService` read
  `SessionContext`/the derived directory instead of minting their own id.
- **Row insert + the weather-bucket trap.** `SessionManager` inserts the `sessions` row on the first
  frame — `track_id`/`car_id` are guaranteed present then (ADR-0008). But **`weather_bucket` is NOT
  trustworthy at the first frame**: ADR-0008 documents `roadTemp`/`airTemp` reading `0` for ~21 s
  after going LIVE, and `DeriveWeatherBucket` mis-buckets the warm-up window. Since `[references]` is
  keyed on `(track,car,weather_bucket)`, freezing a wrong bucket would poison PB matching. So the
  bucket is **provisional at insert and recomputed authoritatively at session finalize** from a
  representative stable-temp window; C7 reference selection reads the *finalized* bucket. (ADR-0008
  guarantees identity at first frame — it does **not** make weather correct there.)
- On stream completion `SessionManager` finalizes the row: `ended_at_utc`, the authoritative
  `weather_bucket`, and `lap_count`/`clean_lap_count`/`pb_time_ms` — the counts are read from the
  persisted `laps` rows (i.e. from `ComputeService`'s output), so finalize must run after compute has
  drained. `ComputeService` writes `laps` rows against `SessionContext.SessionId` (FK satisfied — the
  `sessions` row exists from the first frame).

### `mcap_path` is the session directory; no physical concatenation

- `sessions.mcap_path` stores the **session directory** (which holds `segment-*.mcap`), not a
  single file. Consumers (Parquet conversion, replay, debrief) enumerate `segment-*.mcap` in that
  directory, ordered, and treat them as one logical stream.
- The segment-enumeration logic already exists in `McapReplaySource.ResolveSegmentPaths` (globs
  `*.mcap`, sorts ordinal). It is extracted to a shared `McapSegmentEnumerator` so the replay
  source and the Phase-2 Parquet converter share one implementation (DRY). **No `raw.mcap` is
  ever produced.**

### Canonical recordings layout = the shipped code path

- The canonical layout is the one the code already writes:
  `%LOCALAPPDATA%/SimCoach/recordings/<sessionId>/segment-NNNN.mcap`, with `laps.parquet` and
  `debrief.md` co-located in the same `<sessionId>` directory. `data-model.md` is corrected to
  match the code; the recorder is not changed except to take its directory from `SessionContext`.

## Why

- **Single responsibility / single source of truth**: the producer owns the id; one Storage
  component owns the `sessions` row + directory; recorder and compute are pure consumers. No
  cross-component id agreement problem, no NOT-NULL-at-first-frame problem.
- **Allocate-before-publish kills the race**: resolving identity before frame #1 is published means
  no consumer ever blocks on `Ready` or sheds start-of-stint frames waiting for another subscriber.
- **ADR-0008 guarantees identity, not weather**: `track_id`/`car_id` are present at the first frame
  (so no empty-identity rows), but temps lag ~21 s — hence `weather_bucket` is finalized later, off
  the warm-up window, before any reference is keyed on it.
- **Directory-as-path matches reality**: recordings are inherently multi-segment; a logical stream
  over a directory is simpler and truthful versus a concatenation step that never existed.
- **Docs follow code**: the shipped, Phase-1-tested recorder path is the source of truth; aligning
  docs to it is lower-risk than rewriting working code to match a doc.

## Tradeoffs

- `McapRecorderService` gains a dependency on `SessionContext` (small refactor of Phase-1 code:
  drop private `sessionId` minting, take the directory from context). The recorder's crash-restart
  uniqueness rationale moves with the id onto the producer/`SessionManager` and is pinned by a test.
- `weather_bucket` is provisional between insert and finalize. Mid-session queries see a possibly
  wrong bucket; only the finalized value is authoritative, and only it feeds reference keying. A
  session whose temps never settle (`roadTemp` stuck ≤ 0) finalizes to the documented dry-warm
  fallback — no worse than today.
- A crash mid-session leaves a row with null `ended_at_utc` (and provisional weather) — acceptable
  and detectable (recovery/cleanup can finalize or mark abandoned rows later).
- `SessionManager` subscribes to the fan-out, adding one more consumer; the drop-oldest bounded
  subscription model already supports N consumers. Finalize must run after `ComputeService` drains
  (counts come from persisted `laps`), which the C9 hosted-service stop ordering must honor.

## Consequences

- `IngestService` allocates `SessionId` + resolves `SessionContext.Ready` at stream start, before
  publishing frame #1 (small Pipeline change).
- Phase-2 `C2` delivers `SessionManager` + `SessionContext` (id from the producer; row + directory
  in Storage) alongside the SQLite foundation.
- `McapRecorderService` is refactored to consume `SessionContext` (Phase-1 code touch).
- `McapSegmentEnumerator` is extracted from `McapReplaySource` and reused by the Parquet converter.
- `data-model.md` is updated: `recordings/<sessionId>/` layout, `mcap_path` = directory, `raw.mcap`
  removed, and `laps.raw_offset_in_mcap` reconciled with the segment-directory model (it identifies
  `(segment, offset)`, or is dropped for Phase 2 — a bare whole-session byte offset is meaningless
  without a concatenated file).
