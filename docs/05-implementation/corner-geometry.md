# Corner Geometry — First-Party Baked Centerline (ADR-0014)

Tracks the redesign that replaces the corner-geometry **source** (vendored CrewChief landmarks +
mid-session derive) with first-party geometry baked offline from an aggregate centerline.
Decision and rationale: [ADR-0014](../02-architecture/adr/0014-first-party-baked-corner-geometry.md).
Status legend: `[ ]` todo, `[x]` done.

This changes only the **geometry** half of the pipeline. The **measurement** half (`CornerTracker`
window → `BrakeKernels`/`ThrottleSpeedKernels`/`BalanceKernels` → `CornerEventBuilder`, plus
`GridMetrics` and sector deltas) is untouched — it consumes the abstract `Corner` record, not landmarks.

## Why (short)

- The original premise that justified a rewrite — "CrewChief windows sit 170–320 m before the apex /
  Rettifilo at 268 km/h / 5-of-7 miss the apex" — is **refuted** (see ADR-0014): under the runtime
  normalization all 7 Monza windows contain their apex. We do not build on it.
- The real reasons: **finite coverage** (uncovered tracks, incl. much DLC, get no corners at session
  start); the **skill-contaminated derive fallback** (geometry from one clean lap — ADR-0010's own
  accepted residual risk); **window-width / center drift** up to ~75 m on covered tracks;
  **100% first-party** (drop the vendored CrewChief data); and **flat-corner detection** (large-radius
  corners are found on lateral load even where per-lap curvature is noise).

## What's verified (research + adversarial review, on real Spa/Monza recordings)

| Claim | Status |
|---|---|
| Flat-corner blind spot is an operation-**order** artifact (aggregate position first, differentiate once) | HOLDS |
| Detector does not regress on Monza (7 complexes; Curva Grande on lateral-G alone) | HOLDS |
| Close complexes split on curvature sign-change + load valley | HOLDS |
| Offline NCP↔world coherence is sub-metre (median aggregation, ≥3 laps) | HOLDS |
| All required sign-stable channels present (`world_pos`, `g_force_g`, NCP, …; no native yaw/heading) | HOLDS |
| `find_peaks` prominence auto-kills phantoms | WEAKENED → not relied on |
| Confidence gate ⇒ near-zero human review | WEAKENED → advisory only |

Net posture: **triggered one-glance human review**, not 100% full-auto.

## Data & CI policy

- Telemetry recordings (~45 MB each) are **never committed** and never fetched by CI.
- Per-PR CI gates on **hermetic tests over synthetic frames** + validation of the committed
  `cornerGeometry.json`. There is **no re-bake from raw telemetry on CI**.
- The bake is a deliberate **local, human-reviewed** step that commits a small JSON; provenance
  (lap count, source recording, schema version, lap length) is stamped into the document.

## Bake workflow

```
dotnet run --project tools/SimCoach.Bake -- [recordings-root] [output-dir]
```
Defaults: `recordings-root` = `%LOCALAPPDATA%/SimCoach/recordings`, `output-dir` = current directory.
Bake straight into the vendored data dir, e.g.:
```
dotnet run --project tools/SimCoach.Bake -- "%LOCALAPPDATA%\SimCoach\recordings" "src\SimCoach.Reference\Data"
```

The tool **scans every recording under the root, pools all CLEAN laps per track across all sessions**, and
for each track with ≥ 3 clean laps writes `cornerGeometry.<trackId>.json` + an HTML review page. One run bakes
every covered track at once. Always using all recordings means more clean laps → a more robust median
centerline and fewer single-lap/line artifacts (e.g. a mid-corner correction on one session averages out).

**Clean laps only.** Here "clean" = **the lap was never invalidated by track limits** (`is_valid_lap` true on
every frame) — riding kerbs is fine; only off-track-limit excursions are excluded, because they bias the
centerline (ADR-0010/0014). (This is deliberately *not* `CleanLapPredicate`/`CompletedLap.IsClean`, which also
demands `tyres_out == 0` every frame and so rejects every normal racing lap.) A track with < 3 such laps is
**NO-GO** (skipped, with a reason). Console prints `<track>: N clean lap(s) of M recorded … GO=`. So drive ≥ 3
laps per track without track-limits (any pace, corners with real load; no spins/pit mid-lap) — across sessions
is fine, they need not be in one recording.

Geometry is **one file per track** (`cornerGeometry.<trackId>.json`); the loader embeds all of them via the
`Data\cornerGeometry.*.json` glob and indexes by `trackId`. Open each HTML, confirm the apexes sit on the real
corners, then commit the JSON(s) under `src/SimCoach.Reference/Data/` (the review `.html` is git-ignored there).

### After every bake: add/update corner NAMES (do not skip)

Corner ids are positional (`<trackId>_tNN`) and **depend on the bake** — a re-bake can change the count or
shift ids. So whenever you bake or re-bake a track, **add or update that track's entry in
`src/SimCoach.Coach/Data/cornerNames.json`** (`corner_id → human name`, the Phase-3 prompt layer) to match
the new ids, and review the names against the HTML. Geometry stays nameless in compute; names live only here.
Commit the names change together with the geometry.

### Manual review overrides (rare)

If a clean-lap bake still shows a corner you know is a single-lap/line artifact (e.g. a mid-corner throttle
correction read as a second apex), you may hand-merge/adjust the entries in the committed
`cornerGeometry.<trackId>.json` — this is the "triggered one-glance review" override. Note it is **not durable
across a re-bake** (the detector regenerates it); prefer fixing it at the source with cleaner laps. Re-number
ids after a merge and update `cornerNames.json` to match.

## Phases

### Phase 0 — Aggregation core + coherence gate  `[x]`
- [x] `MedianCenterlineBuilder` — median `world_pos` + median `|latG|` per 1 m wrap-segmented bin; median (not mean) rejects single-lap outliers; guards speed/world/teleport.
- [x] `CenterlineCoherence` + `CoherenceReport` — offline GO/NO-GO (median-from-median sub-metre, fail-closed < 3 laps).
- [x] Hermetic tests on `SyntheticSessionBuilder` frames (aggregation onto the circle; bin-0 teleport rejected; trust floor).

### Phase 1 — Detector + bake tool  `[x]`
- [x] `CornerCenterlineDetector` — differentiate once (heading/curvature); fuse curvature (R<180 m) + median `|latG|`≥1.0 g; **apex = geometric centre of the corner extent** (window midpoint, line-independent); radius/trigger from the tightest point.
- [x] Close-complex splitting by **per-lap consensus** (corner-split research): candidate apexes are prominent (≥0.30) fused local maxima per active run; a loaded stretch is cut between two apexes only when the valley is deep (<0.65×), both apexes are tight (R≤180 m), AND ≥60 % of the individual clean laps independently show both apexes — so a real left-right chicane (every lap) splits while a one-lap line artifact does not; a clear de-load valley (<0.55) always separates. Detector takes each clean lap's own centerline. Verified on real data: Monza 11 (Rettifilo/Roggia split, robust to a pooled crash-session lap; Curva Grande merged), Spa 19 (Pouhon=2, Fagnes=2, no t14) — one global config, no per-track tuning.
- [x] `CornerGeometryDocument` / `CornerGeometryEntry` — schema-versioned, length-pinned `cornerGeometry.json` shape (writer + reader share it).
- [x] `tools/SimCoach.Bake` console + `CornerGeometryReviewPage` (HTML/SVG one-glance gate); `bootstrap.sh` adds `tools/*` to the solution.
- [x] Hermetic detector tests (tight corner by curvature; flat R=250 m corner by lateral-G alone; chicane split; no phantom on a straight).
- [x] **Validated bake of Monza** (`20260624-193240-243`, 5 laps): 11 corners = the 11 real turns (the 3 chicanes split into apexes), Curva Grande on lateral-G alone. Committed to `Data/cornerGeometry.json`.

### Phase 2 — Swap the geometry source  `[x]`
- [x] Vendor the baked Monza `cornerGeometry.json` (embedded resource).
- [x] `TrackModelSource.Baked` (enum trimmed to `None`/`Baked`; `Derived` + `DerivedFromLapTimeMs` removed).
- [x] `CornerGeometryDataset` — read-only loader: schema-version pin + lap-length check + ADR-0010 range guard (`0 ≤ start < end ≤ 1`; one bad range disqualifies the track); maps entries → `Corner`.
- [x] Rewire `TrackModelStore.Get` to `Baked → None`; delete the mid-session `Derive` block in `ComputeSession`; rewire DI in `TelemetryComposition`.
- [x] Delete the CrewChief/derive surface: `LandmarkDataset`, `TrackModelBuilder`, `ITrackModelRepository`/`JsonTrackModelRepository`, `Data/trackLandmarksData.json` + `Data/LICENSE-CrewChief`, and their tests.

### Phase 3 — Golden + ADR supersession + docs  `[x]`
- [x] `Phase2ComputeE2EGoldenTests` builds the store from a baked test fixture (`BakedGeometryFixture.Spa()`); structural assertions unchanged (synthetic `world_pos` is a perfect circle, not a geometry oracle).
- [x] ADR-0010 marked `Superseded by ADR-0014`; KB references updated (`.gitignore` negation comment, INDEX, build-quirks). The older `phase-2-detailed-plan.md` is kept as a historical record.

## Status

All four phases are done. Monza (11 corners) and Spa (19 corners) are baked from live clean laps
(pooled across sessions) and committed, both tracks named.

Open / done:
- ~~Live NCP / lap-wrap sync (first live Monza gave 0 laps).~~ **Effectively met:** live lap detection
  fired across multiple live Spa and Monza sessions; both shipped geometries are baked from those live
  recordings (the 0-laps case no longer reproduces). A separate live app crash on a pit-return duplicate
  `lap_number` is tracked as issue #13 (not a geometry concern).
- ~~Re-bake Monza from a fresh live recording.~~ **Done** (re-baked with the per-lap-consensus splitter).
- **Still open:** validate on a genuine third / DLC track and across drivers (only Spa + Monza, one
  driver so far); the detector's single global config is unproven beyond these two circuits.
- ~~First-party corner-naming source.~~ **Done:** authored `CornerNameMap`
  (`src/SimCoach.Coach/Data/cornerNames.json`) at the prompt layer maps baked `corner_id → name` for
  Monza and Spa; geometry (compute) stays nameless. Re-author per track when (re)baked.

## Validated Monza bake (evidence)

11 corners from 5 laps of `20260624-193240-243`, apex in metres (× lap length 5793):

| id | apex (m) | R (m) | peak \|G\| | trigger | turn |
|---|---|---|---|---|---|
| monza_t01 | 936 | 24 | 1.06 | Both | Rettifilo (1) |
| monza_t02 | 978 | 19 | 1.31 | Both | Rettifilo (2) |
| monza_t03 | 1551 | 208 | 1.45 | **LateralG** | Curva Grande |
| monza_t04 | 2159 | 48 | 1.42 | Both | Roggia (1) |
| monza_t05 | 2190 | 34 | 1.19 | Both | Roggia (2) |
| monza_t06 | 2576 | 77 | 1.65 | Both | Lesmo 1 |
| monza_t07 | 2896 | 65 | 1.69 | Both | Lesmo 2 |
| monza_t08 | 3974 | 86 | 1.38 | Both | Ascari (1) |
| monza_t09 | 4049 | 84 | 1.47 | Both | Ascari (2) |
| monza_t10 | 4153 | 116 | 1.44 | Both | Ascari (3) |
| monza_t11 | 5236 | 81 | 1.75 | Both | Parabolica |

Curva Grande (R = 208 m > 180 m) is found on lateral load alone — the central insight, on live data.
