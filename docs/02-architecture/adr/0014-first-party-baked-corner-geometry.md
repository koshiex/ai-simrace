# ADR-0014: First-party baked corner geometry from an aggregate centerline (supersedes ADR-0010 sourcing)

**Status**: Accepted
**Date**: 2026-06-26

## Context

[ADR-0010](0010-corner-model-from-vendored-landmarks.md) sources corner *geometry* (entry/apex/exit by
`normalized_car_position`) from a vendored MIT CrewChief landmark dataset, with a **derive-from-the-driver's-lap
fallback** for uncovered tracks, and keeps names at the prompt layer. `TrackModelStore.Get` resolves a
`TrackModel` in priority order `dataset → persisted derived → none`; the resolved `Corner` records drive
`CornerTracker`, whose window feeds the kernels. Sectors come from the sim, not the model.

Three things forced a revisit.

**1. The original "the windows are wrong" premise was false — and we record that so it is not rebuilt.**
An earlier analysis claimed the CrewChief Monza windows sit ~170–320 m *before* the apex (e.g. "Rettifilo
window ≈ 268 km/h vs a ~47 km/h apex at ~943 m; 5 of 7 windows miss the apex"). On re-checking against the
runtime this **does not hold**. The configured Monza length is `5793 m`
(`AccTrackCatalog.cs`), `CornerTracker` matches on `NormalizedCarPosition` (field 8), and
`AccFrameMapper` defines `LapDistanceM = NCP × lapLengthM`, so NCP↔distance carry zero offset (verified
0.000 m over 329k frames). The committed `trackLandmarksData.json` Monza key `monza:track config` puts
`rettifilo` at 890–990 m → NCP [0.1536, 0.1709], which **contains** the measured apex (NCP 0.1695, ~52 km/h
at ~982 m). **All 7/7 Monza windows contain their apex**; the only residual is center-vs-apex drift of
−75…+49 m (the dataset's `ApexPosition` is the window midpoint, not the geometric apex). The "268 km/h"
figure is reproducible only under an `L ≈ 8271 m` normalization that does not exist in the runtime — an
artifact of an early wrong length. **We do not justify this change by "the windows are misplaced."**

**2. The derive fallback ties geometry to one driver's lap — ADR-0010's own accepted residual risk.**
ADR-0010 §Why already states: on uncovered tracks "the derive fallback still ties geometry to the driver's
lap — that residual risk is real … and shrinks as dataset coverage grows," and §Context calls geometry from
a slow/erratic driver's lap "the exact failure that makes the product pointless." `TrackModelBuilder` builds
each corner from a *single* clean lap's braking zones (`Start`=brake-on, `Apex`=min-speed, `End`=throttle-on)
and is brake-gated, so it also misses no-brake corners (Monza `curva_grande`/`lesmo2`/`roggia` are taken at
`maxBrake ≈ 0`, `|latG|` 1.35–1.53). CrewChief ACC coverage is partial (Spa/Barcelona/Monza confirmed, uneven
beyond), so every uncovered track — including much DLC — runs on this skill-contaminated, single-lap,
brake-gated path. That is the real defect.

**3. We want one first-party geometry source for all tracks.** No per-track reference lab, full ACC coverage
including DLC, a single clean source of truth, and no vendored third-party data in the shipped artifact.

The data needed is already recorded. Telemetry (`telemetry.proto` `TelemetryFrame`) carries `carCoordinates`
(field 32, world x/z), `lap_distance_m` (7), `normalized_car_position` (8), `g_force` (23, `.x` = lateral),
`steer_rad` (13), `speed_mps` (9, **m/s, not km/h**). There is **no native yaw-rate or heading** field (the
proto stops at field 36), so heading must be derived from `world_pos`. Lap boundaries come from position-wrap
(ADR-0012), never the `lap_number` field, which lumps physical laps.

## Decision

**Source corner geometry from first-party telemetry, baked offline into a vendored `cornerGeometry.json`, by
aggregating position FIRST and differentiating ONCE. Delete the CrewChief dataset and the mid-session derive.
The measurement half of the pipeline is unchanged.**

1. **Aggregate-position-first centerline.** Segment laps by position-wrap; per ~1 m distance bin take the
   **median** `world_pos` and **median** `|latG|` across laps → one smooth corridor centerline. Median (not
   mean) is mandatory: a mean is poisoned by start/finish teleport frames (a single lap up to 277 m off at
   bin 0) and off-track laps (20–34 m). Require ≥3 (warn < 5) full laps per bin; guard the S/F bin-0 frame;
   clamp non-physical `|G| > 5 g` (ADR-0013 spirit). `speed` is read as m/s.

2. **Differentiate once, detect on sign-stable channels.** Compute heading `θ = atan2(Δz, Δx)` and signed
   curvature `κ` from the *median* centerline (one differentiation). Detect corners by **median `|latG|`** and
   **total-variation-of-heading** `∫|dθ|` (always positive) — both sign-insensitive — thresholded at
   `|G| ≥ 1.0 g OR R < 180 m`. Split close complexes at a curvature **sign change** (±0.004 rad/m, ~R250)
   co-located with a `|G|` valley (0.65× of the flanking peaks), but only when both flanking peaks are real
   (fused load ≥ 1.25) and prominent (peak-to-valley drop ≥ 0.35) and ≥ 40 m apart — otherwise a single
   continuous corner over-fragments (e.g. the Eau Rouge/Raidillon esse stays one complex). Post-split
   `min-arc ≥ 35 m` too. **Apex = the geometric centre of the corner extent** (window midpoint): line-
   independent, so one driver's early-apex hotlap line does not drag the apex toward the entry. The window's
   tightest point (max `|κ|`) gives the corner radius/trigger. Window = brake-onset → apex → throttle-resume.
   `Corner` positions are normalized 0..1 (divide by track length); the ADR-0010 range guard
   (`0 ≤ start < end ≤ lapLength`; one bad range disqualifies the track) is kept.

3. **Bake → vendored JSON → loader.** A dotnet bake tool runs the detector over a recording and emits
   `cornerGeometry.json` (normalized `Corner` records, schema version, pinned `lapLengthM`, `trackId`,
   `source = Baked`, lap-count provenance) plus a static HTML/SVG review page. A read-only
   `CornerGeometryDataset` loads it. `TrackModelStore.Get` priority becomes **`Baked → None`** (a track with
   no bake yet has its corners suppressed, exactly as `None` does today). `TrackModelSource` gains `Baked`.

4. **Delete the old geometry sourcing.** Remove `LandmarkDataset`, `TrackModelBuilder`, the
   `ITrackModelRepository`/`JsonTrackModelRepository` persistence, the vendored `trackLandmarksData.json` +
   its MIT notice, and the mid-session derive/rebuild block in `ComputeSession`. Geometry is now **fixed for
   the session**. The shipped artifact becomes 100% first-party.

5. **Geometry stays decoupled from the measurement half.** `CornerTracker`, `BrakeKernels`,
   `ThrottleSpeedKernels`, `BalanceKernels`, `CornerEventBuilder`, `GridMetrics`, sector deltas, and the
   `Corner`/`TrackModel` record shapes are **unchanged** — they consume the abstract `Corner` record, not
   landmarks. `corner_id` becomes `<track_id>_t01..NN` positional (names are out of compute, per ADR-0010);
   the churn is harmless (references are keyed by the position grid, not `corner_id`).

### Human review is triggered, not eliminated

The baked JSON is committed only after a **one-glance** human look at the generated HTML/SVG page (centerline
+ corner windows + apexes + `|latG|` trace). Two automation claims were tested and **did not hold strongly
enough to remove the human**: prominence peak-picking does not reliably auto-kill phantoms (Spa over-detects
~27 for ~20 turns), and a cross-lap apex-confidence gate is too threshold-sensitive (fires on 43–71%, invalid
CI at N = 3–5 laps) to be an automatic accept/reject. The confidence metric ships only as an **advisory
sort-order** on the review page (most-scattered corners first). This is honestly "triggered one-glance
review," **not** "100% full-auto geometry."

## Why

- **Geometry decoupled from driver skill, on ALL tracks.** ADR-0010 achieved this only on dataset-covered
  tracks and explicitly accepted the derive-fallback skill risk on the rest. Aggregating many laps' positions
  with a median is robust to one bad lap and never anchored to a single driver's braking. This closes the
  exact residual risk ADR-0010 flagged.
- **The order-of-operations fix is real, demonstrated, and the root reason a first-party detector works.**
  Differentiation is high-pass; computing signed `1/R` on each noisy per-lap path then aggregating is
  ill-conditioned at fast/flat large-radius corners, where the per-lap sign is noise-dominated and the
  aggregate cancels. Reversing the order recovers them: at **Eau Rouge** the old per-lap-signed median is
  +0.0001 ≈ 0 (corner erased; two laps return a physically impossible R = 17 m at ~250 km/h), while the
  centerline gives R ≈ 160 m; at **Raidillon** the old mean flips sign while the new centerline gives R = 81 m
  backed by a sign-stable −3.0…−3.7 g every lap. The mechanism (curvature noise ~ `σ_pos/S²`, CV up to ~11 at
  small span) **replicates on Monza**, a track not used to derive it; at tight corners both orders agree
  (La Source R = 23 m both). Detection uses the sign-insensitive `|latG|` + heading-TV channels, so it never
  depends on signed-curvature sign at the flattest corners (where even the centerline sign is not reproducible
  across sessions).
- **No regression on covered geometry.** On Monza the detector returns exactly the 7 real complexes on a broad
  threshold plateau (not a tuned point), apex vs min-speed within 2–22 m, and resolves Curva Grande
  (R = 237 m) on the `|latG|` channel alone — the fast large-radius corner pure curvature would miss. Roggia
  and the two Lesmos stay distinct; close complexes (Combes/Malmedy/Rivage, Bruxelles/Pouhon) split at genuine
  curvature reversals in `|G|` valleys (reversals an order of magnitude above the gate; generalizes to an
  independent Monza session).
- **Offline aggregation is trustworthy.** NCP↔distance↔world is coherent: median-from-median per-bin deviation
  is sub-meter (Spa 0.52 m, Monza 0.33–0.37 m), invariant to bin width and to binning channel; the large
  per-bin spreads are single-lap outliers the median rejects.
- **All required channels exist and are physical** (verified against the proto and a frame-tag dump); the
  detector needs none that are missing — it derives heading from `world_pos` and never reads native yaw.
- **Licensing simplifies to first-party.** Removing the vendored CrewChief file and notice makes the shipped
  artifact 100% first-party telemetry. External datasets (CrewChief, OSM, SimHub) are demoted to **private
  cross-check only** (count/name sanity), never shipped — avoiding ODbL share-alike and name copyright.
- **ADR-0010's general truths are preserved**, so this supersedes only its *sourcing*: geometry decoupled from
  skill, all-tracks-without-hand-authoring, names at the prompt layer, sectors from the sim, the range-sanity
  guard, and harmless `corner_id` churn all still hold.

## Tradeoffs

- **Not full-auto.** Baking a track triggers a one-glance human review before its JSON is committed
  (prominence does not auto-kill all phantoms; the confidence gate is advisory, not automatic). We deliberately
  do **not** market this as hands-off geometry.
- **The flat-corner win mostly helps the uncovered/derive path, not covered tracks.** On covered tracks the
  CrewChief windows already contain their apex; the measurable covered-track gain is tightening the apex from
  the window midpoint (drift up to ~75 m) to argmax-curvature (±2–22 m). The headline benefit is coverage +
  killing the skill-contaminated derive + first-party, not accuracy on Monza.
- **Thin, biased validation.** Evidence is two tracks (Spa, Monza), one driver, 3–6 laps each — same-driver
  multi-lap consistency, not cross-driver, and not a genuine third/DLC track. The detector constants
  (R = 180 m, 1.0 g, 35 m min-arc, fusion-normalization 1/15 m and 2.5 g) are physical scales but unproven on
  a held-out circuit.
- **The split over-fragments by default** (min-arc applies to baseline arcs, not split products), so the
  post-split min-arc + min-load filter is load-bearing and itself only tested on Spa/Monza; same-direction
  double-apex splitting is unproven.
- **Implementation traps that bite silently:** `speed` is field 9 in **m/s** (a km/h reading is 3.6× off);
  there is **no native heading/yaw** (derive from `world_pos`); a recording missing `world_pos` (an older
  build) cannot be baked; the S/F bin-0 frame and non-physical `|G|` spikes must be guarded; **mean** binning
  is poisoned — only **median** is safe.
- **Golden churn.** `Phase2ComputeE2EGoldenTests` builds the store from `LandmarkDataset` + the repository and
  asserts a `Corner` event on a synthetic Spa replay; it must be rebuilt against a baked test fixture
  (the synthetic `world_pos` is a perfect circle, not a geometry oracle).

## Precondition (open)

**Live NCP/lap-wrap sync is unverified and gates trusting any bake.** The first live Monza run produced 0 laps
from a lap-detection desync; the PR1 wrap-primary fix (ADR-0012) is committed but not yet confirmed to assign
frames to the right lap in real time, and every centerline bin keys on a correct live NCP↔world mapping.
Offline coherence (above) does **not** prove live binning stays in sync. No track's bake is trusted until a
live ≥3-lap clean recording is captured and re-baked end-to-end.

## Consequences

- **New**: `MedianCenterlineBuilder`, `CornerCenterlineDetector`, `CornerGeometryDataset`,
  `Data/cornerGeometry.json` (Monza first), a `tools/SimCoach.Bake` console (bake + HTML/SVG review), and
  their tests + real-recording fixtures. `TrackModelSource` gains `Baked`.
- **Removed**: `LandmarkDataset`, `TrackModelBuilder`, `ITrackModelRepository`/`JsonTrackModelRepository`,
  `Data/trackLandmarksData.json` + `Data/LICENSE-CrewChief`, their tests, the KB `landmark-dataset.md`, and
  `ComputeSession`'s mid-session derive/rebuild block. `TrackModelSource.Derived`/`DerivedFromLapTimeMs` become
  dead.
- **Changed**: `TrackModelStore` priority `Baked → None` (no derive); `ComputeSession` geometry fixed per
  session; DI in `TelemetryComposition`; the Phase-2 golden.
- **Offline observability** (correctness trackable without launching the game): an executable NCP/world
  coherence gate (GO/NO-GO, fail-closed < 3 laps), a detector parity/stability check against the committed
  fixtures and the Python oracle, and the bake tool's diffable HTML/SVG artifact with JSON provenance.
- **ADR-0010** is superseded on **sourcing only** and remains in force until the bake path ships; it is marked
  `Superseded by ADR-0014` when the CrewChief surface is deleted (the geometry-swap PR). Phase-3 still owns
  `corner_id → name`, now from an independent first-party source (open item; baked JSON ships unnamed).
- Relates to [ADR-0012](0012-lap-boundary-from-position-wrap.md) (wrap segmentation this depends on) and
  [ADR-0013](0013-clamp-non-monotonic-laps-in-parquet.md) (the `|G|`/position outlier handling it mirrors).
