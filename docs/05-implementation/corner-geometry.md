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
dotnet run --project tools/SimCoach.Bake -- <recording-dir> [output path]
```

Reads the recording's MCAP, segments laps by position-wrap, runs the offline coherence gate
(**refuses to bake on NO-GO** or < 3 laps), builds the median centerline, detects corners, and writes
the geometry JSON + a static HTML review page. Geometry is **one file per track**:
`cornerGeometry.<trackId>.json` (the default output name; the loader embeds all of them via the
`Data\cornerGeometry.*.json` glob and indexes by `trackId`, so a bake never overwrites another track).
Open the HTML, confirm the apexes sit on the real corners, then commit the JSON to
`src/SimCoach.Reference/Data/` (e.g. bake straight to `src\SimCoach.Reference\Data\cornerGeometry.spa.json`).

## Phases

### Phase 0 — Aggregation core + coherence gate  `[x]`
- [x] `MedianCenterlineBuilder` — median `world_pos` + median `|latG|` per 1 m wrap-segmented bin; median (not mean) rejects single-lap outliers; guards speed/world/teleport.
- [x] `CenterlineCoherence` + `CoherenceReport` — offline GO/NO-GO (median-from-median sub-metre, fail-closed < 3 laps).
- [x] Hermetic tests on `SyntheticSessionBuilder` frames (aggregation onto the circle; bin-0 teleport rejected; trust floor).

### Phase 1 — Detector + bake tool  `[x]`
- [x] `CornerCenterlineDetector` — differentiate once (heading/curvature); fuse curvature (R<180 m) + median `|latG|`≥1.0 g; apex = argmax `|curvature|`.
- [x] Close-complex splitting (curvature sign-change ±0.0015 rad/m or load valley < 0.65× flank); post-split min-arc.
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

## Open items (require a live ACC run)

- **Live NCP / lap-wrap sync** is the hard precondition (first live Monza gave 0 laps; PR1 wrap fix
  committed but not live-verified). No bake is trusted until a live ≥3-lap clean recording reproduces
  the geometry end-to-end.
- Re-bake Monza from a fresh live recording once the live pipeline is confirmed.
- Validate on a genuine third / DLC track and across drivers.
- ~~Decide a first-party corner-naming source for Phase 3.~~ **Done:** first-party authored
  `CornerNameMap` (`src/SimCoach.Coach/Data/cornerNames.json`) at the prompt layer maps baked
  `corner_id → name` (Monza named). Corner names are public facts, authored by us — not from any
  third-party dataset; geometry (compute) stays nameless. Re-author per track when (re)baked.

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
