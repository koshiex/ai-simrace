# Detailed Plan — Phase 2 (Reference Laps + Deterministic Compute)

Expands `implementation-plan.md` Phase 2 into ordered, testable steps.
Status legend: `[ ]` todo, `[x]` done.

Phase 1 closed (B1–B7 + MCAP zstd/summary follow-up). Every recorded frame is now LIVE and
identity-populated (ADR-0008), so all telemetry reaching compute is keyable by `track_id`/`car_id`.

---

## Goal

From a recorded/replayed session, deterministically derive lap/sector/corner structure, compute
per-corner driving metrics, select and store reference (PB) laps, and emit the four domain events
(`CornerEvent`, `SectorEvent`, `LapEvent`, `SessionEvent`) that Phase 3's coach engine consumes —
all offline-testable on macOS via the `McapReplaySource` harness.

## Design decisions (taken before this plan)

| Decision | Rationale |
|---|---|
| **Add the missing telemetry fields** (`world_pos`, `current_sector_index`, `sector_count`) rather than work around their absence | No crutches: racing-line deviation and reliable sector segmentation need real data. The decision is independently cheap because the source structs already marshal these fields (mapper-only, no struct-layout change — see C1); scaling later beats unwinding a workaround. |
| **Corner geometry from a corner-landmark dataset where covered; derive from the driver's lap as fallback; sectors always from the sim** (ADR-0010) | "All tracks" with zero hand-authoring. **Caveat:** the "geometry decoupled from driver skill" benefit holds *only for dataset-covered tracks*; uncovered tracks fall back to deriving geometry from the driver's own fastest clean lap, so a weak driver can still misplace corners there (see risk register). Sector splits always come from the sim's own `current_sector_index`, every track. |
| **Corner names live at the Phase-3 prompt layer, never in compute** (ADR-0010) | Compute emits only the stable `corner_id` token; the Coach `PromptBuilder` injects a `corner_id → name` map so the LLM says "Eau Rouge". Keeps compute sim-agnostic; `corner_id` churn is harmless (no corner table; references keyed by position grid). |
| **Landmark dataset is MIT-licensed → vendored with its notice** (ADR-0010) | CrewChief's `trackLandmarksData.json` is **MIT** (Copyright (c) 2019-2022 Britton IT Ltd), verified on the canonical repo `gitlab.com/mr_belowski/CrewChiefV4` (the GitHub mirror is stale/migrated and lacks a project LICENSE, hence GitHub's mis-detected badge). Redistributable provided the MIT notice is retained. |
| **Session id allocated by the producer before frame #1; `SessionManager` owns the row + directory; `mcap_path` = directory; no physical MCAP concat** (ADR-0011) | `IngestService` resolves `SessionContext` before publishing, so recorder/compute never race for identity. `SessionManager` creates the dir and inserts the row at first frame (identity per ADR-0008), with `weather_bucket` finalized off the ~21 s temp warm-up window (else the `[references]` triple is poisoned). |
| **Parquet conversion on session end, over the session's segment directory** | `laps.parquet` is an async conversion at session end. A session is a *directory of rotating `segment-*.mcap`*, not one file — the converter enumerates segments in order (shared `McapSegmentEnumerator`, ADR-0011). Keeps the hot path (compute) free of Parquet I/O. |
| **Reference = fastest *clean* lap per `(track,car,weather)` triple** | Matches `[references]` UNIQUE constraint and the `pinned` guard in `data-model.md`. |
| **Delta vs reference computed by time-at-position** | Resample both laps onto a 1 m position grid; `delta_ms(p)` = self time − ref time at the same normalized position. Robust to frame-rate jitter. |

Build-order dependency: **C1 → {C2 ∥ C3} → C4 → C5 → C6 → C7 → C8 → C9**.

Runtime data dependency (separate from build order): C5's *derive* path consumes C3 segmentation +
C4 kernels at runtime — it produces a model only after the first clean lap completes, not at session
start. So `C3,C4 → C5(derive)` at runtime, and C8 needs both the C5 model and the C7 reference.

---

### C1. Contract additions + ACC mapping (`Contracts`, `Adapters.ACC`)

Add the fields compute needs, as optional appends (proto rule: append-only, never reorder).
All targets are **already marshalled** by the struct ports — this is mapper-only, no struct change.

- `telemetry.proto` `TelemetryFrame`: `Vec3 world_pos = 32;` (track-frame metres),
  `int32 current_sector_index = 33;` (0-based), `int32 sector_count = 34;`,
  `int32 tyres_out = 35;` (off-track wheel count) and `bool is_valid_lap = 36;` (the sim's own
  lap-validity flag — backs the clean-lap predicate; see C3, fixes the prior "wheels-off proxy").
- `AccFrameMapper`: `world_pos` from `graphics.CarCoordinates[PlayerCarId*3 + {0,1,2}]`,
  `current_sector_index` from `graphics.CurrentSectorIndex`, `sector_count` from
  `static.SectorCount`, `tyres_out` from `physics.NumberOfTyresOut`, `is_valid_lap` from
  `graphics.IsValidLap` (ACC `int`; map `!= 0 → true`, mirroring the existing `tc_active`/`abs_active`
  int→bool conversions — pin with a mapper golden). All five already marshalled — no struct change.
- Update `telemetry-schema.md` (field table + provenance rows).
- **Fixtures:** the committed Phase-1 MCAP fixture predates these fields (they replay as `0`).
  Phase-2 fixtures are **synthesized** (a multi-lap Spa trace built in test code) — real ACC SHM
  captures are still Windows-blocked (Phase-1 B1/B7), so synthesis is the only currently-actionable
  path; regenerate from real dumps later when a Windows capture lands. **The synthesized multi-lap
  compute fixture is a Phase-2 deliverable** so C3–C8 have lap/sector/corner structure to test.
- **Tests:** mapper golden for the new fields (incl. `PlayerCarId` indexing, sector-index edge at
  lap wrap, `tyres_out`/`is_valid_lap`); `Marshal.SizeOf` page goldens unchanged.

### C2. SQLite foundation (`Storage`)

- Embedded schema from `data-model.md`: `sessions`, `laps`, `[references]`, `llm_usage`,
  `settings` + indexes. Idempotent migration runner (versioned, `settings`-tracked or
  `PRAGMA user_version`).
- `SqliteConnectionFactory` (path from config, `%LOCALAPPDATA%/SimCoach/simcoach.db`,
  cross-platform base dir, `foreign_keys=ON`).
- Dapper repositories: `SessionRepository`, `LapRepository`, `ReferenceRepository`,
  `SettingsRepository` — parameterized queries only (no string concat).
- **Session identity + `SessionManager`** (ADR-0011 — note the allocate-before-publish ordering):
  - `IngestService` (producer) allocates `SessionId` (`yyyyMMdd-HHmmss-fff`) at stream start and
    resolves the shared `SessionContext { SessionId, StartedAtUtc }` **before publishing frame #1**
    (`Ready` `TaskCompletionSource` with `RunContinuationsAsynchronously`). This removes the
    inter-subscriber race structurally — no consumer ever blocks on `Ready` or sheds opening frames.
    The ms suffix preserves the recorder's crash-restart-uniqueness invariant (restated + tested).
  - `SessionManager` (`Storage`, `BackgroundService`) owns the `sessions` row + the directory: on
    `Ready` it derives `SessionDirectory = <BasePath>/<SessionId>` and **creates it** (owner creates
    the dir → `mcap_path` always backs a real directory). Inserts the row on the first frame
    (`track_id`/`car_id` present per ADR-0008). **`weather_bucket` is provisional at insert and
    finalized off the ~21 s temp warm-up window** (ADR-0008: temps read 0 early; freezing the bucket
    would poison the `[references]` triple). Finalizes `ended_at`/authoritative `weather_bucket`/
    counts/PB at session end — counts read from persisted `laps`, so finalize runs after compute.
- **Refactor `McapRecorderService`** (Phase-1 code) to take its directory from `SessionContext`
  (drop its private `sessionId` minting). `mcap_path` stores the session *directory*, not a file.
- **Tests:** temp-file/in-memory SQLite; CRUD round-trips; FK cascade on session delete;
  UNIQUE `(session_id, lap_number)` and `(track_id, car_id, weather_bucket)`; migration runs twice
  cleanly; identity resolves before the first publish (no dropped opening frames); two sessions
  started within one second get distinct directories; row inserted at first frame with non-null
  `mcap_path` (dir exists); `weather_bucket` finalized correctly when temps settle after a 0-temp
  start; counts/PB finalized at end; recorder writes under the shared `SessionContext` directory.

### C3. Lap & sector segmentation (`Pipeline`, pure)

- `LapSegmenter` / `SectorSegmenter`: consume the frame stream, emit lap and sector boundaries.
  Lap completion = `lap_number` increment with `normalized_car_position` wrap (1→0) guard;
  sector cross = `current_sector_index` change. Accumulate `lap_time_ms`, `s1/s2/s3_ms`.
- Clean-lap predicate (all from mapped channels, no proxies): `is_valid_lap` held true for the
  whole lap, `tyres_out == 0` throughout (off-track), no penalty/black-flag bit in `flags_active`,
  and the lap fully bounded start-line to start-line (discard partial/out/in laps for reference use).
- Pure, stream-fed, `TimeProvider`-free (uses frame `t`). Seam mirrors `TelemetryFanOut` style.
- **Tests:** synthetic + replay fixture → expected lap count, sector times sum to lap time,
  partial first/last lap discarded, wrap-around handled.

### C4. Deterministic compute kernels (`Pipeline`, pure)

Windowed pure functions over a buffered lap's frames:

- brake-on / brake-off points (threshold + hysteresis constants), peak-brake `brake_pct`,
  trail-brake-% (fraction of a corner with `brake_pct>τ` ∧ `|steer_rad|>τ`), throttle-on point
  (`throttle_pct≥0.5` resume), min-speed and its position.
- understeer / oversteer scores from `wheel_slip` (front vs rear), `steer_rad`, and lateral
  `g_force_g.x` (all native channels). The *score itself* has no native channel — it is a
  documented proxy formula with named-constant thresholds, flagged heuristic.
- racing-line deviation: RMS lateral distance between self path and reference path, matched on
  the 1 m position grid (uses `world_pos`; needs the reference from C7 at emit time).
- **Tests:** hand-built corner traces with known brake/min-speed/throttle answers; degenerate
  laps (no braking, full-throttle) return sentinel/zero without throwing.

### C5. Track-model resolution: vendored landmarks + derive fallback (`Reference`) — see ADR-0010

- **Vendor the MIT-licensed `trackLandmarksData.json`** from `gitlab.com/mr_belowski/CrewChiefV4`
  (MIT, Britton IT Ltd) into the repo together with its MIT notice. `LandmarkDataset` parses
  `{ landmarkName, distanceRoundLapStart, distanceRoundLapEnd }` (metres) → `normalized_car_position`
  via lap length (`AccTrackCatalog` already provides per-track lengths — `trackSPlineLength` is 0);
  alias-map the dataset's `acTrackNames` → our `track_id` (extends `AccTrackCatalog`'s normalization).
- `TrackModelStore.Get(trackId)` resolves a model as follows (log the source: `dataset | derived |
  none`). The **dataset** path resolves at session start; the **derive** path can only resolve
  *after the first clean lap completes* (runtime dependency on C3/C4), so a fresh uncovered track
  starts at `none` and gains corners mid-session:
  1. **Dataset model** — landmark entry exists, ranges sane (`0 ≤ start < end ≤ lapLength`):
     corners from landmarks, `corner_id = <trackId>_<slug>`, names available.
  2. **Derived model** (`TrackModelBuilder`, fallback) — no/invalid entry: corner windows from the
     driver's fastest *clean* lap (brake-on → min-speed → throttle-resume), `corner_id =
     <trackId>_t01..NN` ordered by position, names `null`; idempotent rebuild on a faster clean lap.
  3. **Sectors** — always from the sim (`current_sector_index` transitions + `sector_count`),
     independent of 1/2.
  4. Neither yet → corner events suppressed; sector/lap/session events still emit.
- Persist derived models per `track_id` (JSON beside the reference, or a `track_models` row) so a
  re-derive isn't needed every session. **Corner names stay out of compute** — they are a Phase-3
  prompt asset from the same landmark file.
- **Tests:** dataset path on a covered track (e.g. Spa) → expected named corners with sane ranges;
  out-of-range landmark → that track drops to derive; derive fallback on the Spa fixture lap →
  deterministic corner count/order/IDs, idempotent rebuild; sectors resolved from sim regardless.

### C6. Parquet lap writer + 1 m resampler (`Storage`)

- On session end, convert the session's **segment directory** → `laps.parquet` in the same
  `recordings/<sessionId>/` dir (one row group per lap). The session is a directory of rotating
  `segment-*.mcap`, not one file — enumerate them in order via a shared `McapSegmentEnumerator`
  (extracted from `McapReplaySource.ResolveSegmentPaths`, ADR-0011) and read as one logical stream.
  No `raw.mcap` concatenation is produced.
- `laps.parquet` schema = flat `TelemetryFrame` subset per `data-model.md`, **including
  `world_x/y/z`** (from `world_pos`, needed by C4 racing-line deviation — data-model.md schema
  updated to add these columns). `ParquetSharp` + `Apache.Arrow`.
- `PositionResampler`: resample a lap's channels to 1 sample / 1 m of position → the reference
  grid (monotonic position, fixed-length grid keyed off lap length).
- **Tests:** enumerate a multi-segment session → laps span the segment boundary; write→read
  schema/round-trip incl. world coords; resampler yields a monotonically increasing grid of the
  expected length; non-monotonic input (pit detour) rejected/clamped.

### C7. Reference store + PB selection (`Reference`)

- On a clean `LapEvent` that beats the stored time: write the resampled reference parquet to
  `references/<track>_<car>_<weather>.parquet` and upsert the `[references]` row (UNIQUE triple,
  `pinned` rows never auto-replaced).
- `ReferenceLookup.Get(trackId, carId, weatherBucket)` → resampled reference channels, or `null`
  until a PB exists (first session has no reference; coach stays quiet on deltas).
- **Tests:** PB replaced only when faster ∧ clean ∧ not pinned; pinned reference survives a
  faster lap; lookup returns `null` when absent.

### C8. `ComputeService` + domain-event emission (`Pipeline`)

- `BackgroundService` subscribing to the ingest fan-out in its constructor (mirrors
  `McapRecorderService`). Drives C3 segmentation + C4 kernels against live frames, the C5 track
  model, and the C7 reference.
- Emits, on dedicated output channels (a `DomainEventFanOut` mirroring `TelemetryFanOut`, so
  Phase-3 consumers each get every event): `CornerEvent` at corner exit, `SectorEvent` at sector
  cross, `LapEvent` at finish line, `SessionEvent` at session end. Deltas vs reference via
  time-at-position (C4); sign conventions per `telemetry-schema.md` (positive = slower).
  `top_losses` = top-3 corners by `delta_ms`.
- **Corner-exit trigger** (precise, not a bare threshold): first sustained `throttle_pct ≥ 0.5`
  *after* the corner's min-speed point, with hysteresis (mirrors C4's brake-on/off constants) to
  ignore mid-corner throttle stabs. Reuses C4's throttle-on definition rather than restating it.
- **`SessionEvent` scope for Phase 2:** populate `lap_count`, `clean_lap_count`, `pb_time_ms`,
  `average_lap_ms` (clean laps), and `understeer_trend` (aggregated C4 score). **`stints` is
  descoped → emit `[]`** — `StintSummary.tyre_compound` has no telemetry source (no tyre-compound
  field added in C1) and stint segmentation/tyre-degradation are deferred to a later phase
  (proto3 makes the empty repeated field schema-valid).
- Writes `laps` rows via the C2 `LapRepository`, keyed on `SessionContext.SessionId` (the
  `sessions` row already exists — `SessionManager` owns it, ADR-0011 — so the FK is satisfied);
  ComputeService does **not** create or finalize the session row. `SessionManager` reads the
  persisted `laps` for the session-end counts/PB, so compute must drain before finalize (C9 stop
  ordering).
- **Tests:** replay fixture → expected event sequence and counts; corner-exit fires once per
  corner (no double-fire on throttle stabs); delta-sign correctness on a known-slower lap;
  no `CornerEvent`/delta fields when reference is `null`; `SessionEvent.stints` empty;
  clean cancellation.

### C9. Wiring + end-to-end (`App`)

- `TelemetryComposition`/`Program.cs`: register `SqliteConnectionFactory` + repos, the shared
  `SessionContext` (allocated by `IngestService` before frame #1), `SessionManager`,
  `ComputeService`, `TrackModelStore`, `ReferenceStore`/`ReferenceLookup`, the session-end
  Parquet conversion (shared `McapSegmentEnumerator`), and the `DomainEventFanOut`.
- **Hosted-service stop ordering** (`StopAsync` runs reverse of registration): register
  `SessionManager` *before* `ComputeService` so SessionManager stops last and finalizes the row
  (counts/PB from `laps`) only after compute has drained its events. Cover with an e2e assertion
  that finalized `lap_count` equals the laps actually persisted.
- `appsettings.json`: db path, parquet/reference paths, compute thresholds (brake/throttle/
  trail-brake τ, clean-lap rules), resample metres.
- **E2E test:** replay fixture → compute → assert (a) `laps`/`sessions` rows written,
  (b) `laps.parquet` produced, (c) a reference established for the triple, (d) the emitted
  event stream matches a golden.
- Tick the Phase 2 checklist in `implementation-plan.md`; add KB notes (compute thresholds,
  ACC sector-index/world-coord provenance, any resampling gotchas).

Definition of done: Phase 2 checklist in `implementation-plan.md` fully ticked; CI green
(windows + macos); a multi-lap compute fixture committed; replay → events + reference + parquet
verified end-to-end on macOS.

---

## Risk register (Phase 2)

| Risk | Mitigation |
|---|---|
| Phase-1 fixture lacks the new fields → nothing to test compute against | C1 produces a **synthesized** multi-lap compute fixture as an explicit deliverable before C3+ (real ACC captures are Windows-blocked per Phase 1; regenerate from real dumps when a capture lands). |
| Vendored landmark file drifts from upstream | MIT file (Britton IT Ltd) pinned + MIT notice carried; track upstream `gitlab.com/mr_belowski/CrewChiefV4` manually on refresh (ADR-0010). |
| `world_pos` is track-frame, not a 2D racing line | Project onto the reference polyline matched by position grid; deviation is RMS perpendicular distance. Validate on the fixture; if ACC's frame proves noisy, low-pass before RMS. |
| Position-based delta breaks on pit/out/in laps (non-monotonic position) | Reference + delta use only clean, fully-bounded laps; resampler rejects non-monotonic input. |
| understeer/oversteer *score* is a heuristic (inputs are native, the score is not) | Documented proxy (slip front/rear + steer vs native lateral-g) with named constants; calibrate against fixture, flagged heuristic. |
| ParquetSharp native deps on the win+mac CI matrix | Round-trip test gated to run on both runners early (C6); ParquetSharp 16.1.0 pinned (16.0.0 does not exist — see KB). |
| Vendored landmark dataset has partial ACC coverage | Resolution order falls back to the lap-derived model (nameless) for uncovered tracks; sectors come from the sim regardless. Coverage source logged per session (ADR-0010). |
| Landmark ranges (CrewChief lap-length assumption) mismatch a track version → corner placed wrong | Range sanity check (`0 ≤ start < end ≤ lapLength`) drops the offending track to the derive fallback instead of emitting a misplaced corner. |
| **Uncovered tracks derive geometry from the driver's own lap → a weak driver can misplace corner windows** (the rookie-reference risk, confined to the fallback) | Geometry is decoupled from skill only on dataset-*covered* tracks. On uncovered tracks, derive from the fastest *clean* lap (best available), rebuild on improvement, and keep the corner set advisory for coaching — not a correctness gate. Expanding dataset coverage shrinks this surface. |
| Derived-fallback corner geometry also misses flat/long corners (no clear braking zone) | Sector-relative windows backstop it; advisory only. Covered tracks use the dataset and are unaffected. |
