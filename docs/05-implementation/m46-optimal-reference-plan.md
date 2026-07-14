# M46 — Own-optimal ("theoretical best") reference — implementation plan

Next-PR plan. **Not yet implemented** (owner: blueprint-only for now). This is the shippable, own-data,
non-circular beyond-PB target: stitch the driver's best-ever traversal of each sector into a synthetic
cumulative-time target faster than any single lap they drove.

## Why (measured, not assumed)

Cross-session Monza `bmw_m4_gt3 dry-warm`, 14 clean laps: **PB 113.000 s vs Σ best sectors 111.956 s →
GAP 1.044 s.** Spa 0.624 s. An order of magnitude above the kill threshold the critic feared (<0.2 s). The
target is genuinely beyond PB and **non-circular**: every sub-target was physically driven (unlike the
twice-falsified physics envelope; unlike the M38 self-median that zeroes out for a consistent driver).

## Decided (owner)

- **Granularity: PER-SECTOR.** The 1.044 s is itself a sector number; sector times come from ACC's own timer
  (ADR-0010), ~3 ms seam noise at 2 seams. Per-corner stitching would inject ±0.2–0.8 s over ~22 seams —
  same order as the signal → fabrication. Corner-level "where in the sector" stays PB-relative (falls out
  free). Revisit per-corner only if usage shows flat corner deltas inside a fat sector gap.
- **`MinOptimalGainMs` = user-facing config** (default ~150 ms). Below it, PB already is the target.
- **UX: debrief-only first.** Post-session sector ranking by deficit; live overlay optimal-delta deferred
  (avoids conflating two deltas with the PB delta).

## Open owner decisions (gate later commits, not commit 1)

- Outlier-guard tolerance: the `s1+s2+s3 ≈ lap_time_ms` window that excludes poisoned sector bests (the 3.4 s
  per-session outlier case). Pick the value — too tight shrinks candidates, too loose lets anomalies set
  untouchable targets.
- Recency/staleness of sector bests across BoP/setup/track-grip changes (blueprint defers a recency window as
  YAGNI; `weather_bucket` key + `sector_sources_json` provenance mitigate). Confirm defer-for-now.
- Gold-schema sign-off: new optimal fields leave the machine in Gold JSON (aggregates, consistent with the
  privacy rule, but the schema addition needs owner sign-off per convention).

## Architecture (coherent with existing stack)

- Optimal grid feeds **TIME ONLY** (sector/lap delta, debrief ranking). **PB stays the reference** for
  corner brake/throttle/min-speed and line (M38/ADR-0019): the stitched lap's control channels are three
  laps glued at sector seams — diffing against them would fabricate advice. Enforce by construction: the
  optimal grid is only ever read through `GridMetrics.TimeAt` / its `[^1]` time, never `SliceToFrames`.
- Builder is the cross-session generalisation of the live `ComputeSession._bestSectorMs`: min-per-sector over
  ALL stored clean laps (SQL), stitched on the PB grid as positional spine with a per-sector affine time
  correction so `TMsFromLapStart` stays monotone/seam-continuous and equals Σ best sectors at the last sample.
- `OptimalReferenceBaker` registered **before `SessionManager`** in `TelemetryComposition` (reversed
  stop-order → runs AFTER SessionManager writes `laps.parquet`); plus a catch-up in `StartAsync` so existing
  historical data bakes an optimal without a new drive.

## Commit sequence (each build+test+format green)

1. **Schema layer (decision-free):** migration `007_reference_kind.sql` (rebuild `[references]` with
   `kind TEXT NOT NULL DEFAULT 'pb'` + `sector_sources_json TEXT`, `UNIQUE(track,car,weather,kind)`);
   `ReferenceRow += Kind, SectorSourcesJson`; `ReferenceRepository` upsert conflict target
   `(track,car,weather,kind)` + `GetByTriple(kind="pb")` regression-safe; `ReferenceKind` enum. TDD:
   `Migration007Tests` (fresh + 006→007 upgrade preserves PB as kind='pb', `user_version=7`) +
   `ReferenceRepository` coexistence tests.
2. **Builder:** `OptimalReferenceBuilder` (pure) + `LapRepository.BestSectorsByTriple` +
   `LapParquetReader.ReadLap`; guards (PB exists, gain ≥ `MinOptimalGainMs`, outlier window, idempotent
   rebuild). `OptimalReferenceBuilderTests`.
3. **Baker wiring:** `OptimalReferenceBaker` hosted service + composition order; App.Tests registration-order.
4. **Delta routing + proto + Gold (ONE commit — `CoachStartupValidator` hard-fails on unregistered Gold
   fields):** `SectorEvent.optimal_delta_ms`, `LapEvent.optimal_delta_ms`, `SessionEvent.optimal_gap_ms` +
   `sector_optimal_gap_ms`; `ReferenceLookup.Get(kind)`; `ComputeSession` loads `_optimalReference` (TIME
   only); Gold records + `GoldFieldNames` + `CoachStartupValidator.SampleView` (new fields non-null);
   `DebriefTemplate` ranking line.

Files: see the blueprint list — `src/SimCoach.Storage` (007 sql, Rows, ReferenceRepository, LapRepository,
LapParquetReader), `src/SimCoach.Reference` (ReferenceKind, OptimalReferenceBuilder, OptimalReferenceBaker,
ReferenceLookup, ReferenceTriple, ComputeSession, ComputeOptions), `src/SimCoach.Contracts/Schemas/
telemetry.proto`, `src/SimCoach.App/TelemetryComposition.cs`, `src/SimCoach.Coach` (Gold*, GoldFieldNames,
CoachStartupValidator, DebriefTemplate), and mirrored tests.

## Relation to the ghost/alien-line track

Separate, later. M46 (own data) ships first. The ghost path (`acc-ghost-format-re.md`) can add an external
beyond-PB **LINE** once an alien-focused ghost is harvested and the clock is pinned — complementary, not a
dependency.
