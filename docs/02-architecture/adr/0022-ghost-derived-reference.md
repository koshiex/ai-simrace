# ADR-0022: Ghost-derived centerline and corner reference for the 12 non-Monza/Spa tracks

**Status**: Accepted
**Date**: 2026-07-18

## Context

Owner-baked references (ADR-0014) require the driver to have raced a track: the median centerline
(ADR-0019) and its corner geometry are built from the owner's own clean laps, which carry a real
per-bin lateral-G channel. That gives a trustworthy LINE reference and a corner map whose triggers are
a fusion of curvature and measured lateral load.

Only Monza and Spa have owner-baked assets today. The remaining 12 GT3 tracks have no owner laps at
all, so under the owner-baked model they get no centerline, no corner map, and therefore no line
coaching and no corner tips. Track B closes that gap by deriving the reference for those 12 tracks
entirely from **public accreplay ghost laps** — laps driven by *other* people — with no owner drive and
no driver identity retained.

Two properties of ghost data force a different derivation than the owner-baked path:

- A `.ghost` record carries world position, yaw, and pedals, but **no lateral-G channel and no speed**.
  The lateral load that owner-baked corner detection fuses in simply is not present.
- Each ghost has its own cumulative arc-length axis and its own start phase, so the owner cross-lap
  `floor(LapDistanceM)` binning (which relies on the sim spline's `NormalizedCarPosition·length`) does
  not align ghosts to each other out of the box.

## Decision

For the 12 non-Monza/Spa tracks the reference is **ghost-derived**, with these deliberate differences
from the owner-baked path:

- **Centerline = median of others' fast ghost laps, not the owner's own laps.** The fastest usable
  ghost bootstraps a provisional axis; the remaining K−1 ghosts are projected onto it via
  `CenterlineAligner`; the per-bin median over that common 0..N axis is emitted as
  `centerline.<trackId>.json` (`CenterlineGeometryDocument`, `LapCount=K`). The stored **`LateralG=0`
  on every bin** — ghosts carry no lateral load, and none is fabricated.

- **Corner detection uses the curvature-integral sustained-bend channel, not lateral-G.** Because
  `LateralG=0`, the owner-baked `fused = Max(absK·180, |G|)` collapses to `absK·180`, which only fires
  for tight corners (R ≤ 180 m) and silently drops fast arcs (Curva Grande, Spa T02/T16) that in the
  owner maps exist *only* because of their G signature. The B2 curvature-integral channel (a smoothed
  integral of `|kappa|` over a window — total heading change — added to `CornerCenterlineDetector`,
  calibrated against the owner-baked Monza/Spa oracle) restores those fast bends from geometry alone.

- **Ghost-derived corner maps are intentionally degenerate on two fields.** Every corner in a
  ghost-derived `cornerGeometry.<trackId>.json` carries **`Trigger=Curvature`** (the by-load trigger
  never fires with G=0) and **`PeakLateralG=0`** (there is no measured lateral load to report). This is
  a recognisable, intentional signature that distinguishes a ghost-derived corner map from an
  owner-baked one, where corners may carry `Trigger=ByLoad` and a real `PeakLateralG`.

- **Privacy — only the aggregate ships.** Just like the alien-line vendoring (ADR-0021 addendum), only
  the **derived aggregate** leaves the tooling: the vendored `centerline`/`cornerGeometry`/`alien_line`
  assets. The raw `.ghost` files are never committed, and the source driver name is dropped at parse.
  No per-ghost trace and no identity is retained in any shipped artifact.

## The 2 m alignment guard is informational on ghost tracks

On owner-baked tracks the alien-line import runs a hard median-deviation guard (the derived line must
sit within a small envelope of the centerline it is measured against — Spa 0.52 m / Monza 0.33 m under
the 2 m ceiling). That guard is a genuine cross-check there because the line and the centerline come
from independent data.

On ghost tracks the guard is **self-referential and therefore informational only**: the fastest ghost's
alien line is being aligned to a median centerline that was built from *its own bundle* of ghosts, so a
small deviation is guaranteed by construction and proves nothing. The guard value is still logged and
listed, but it is not the backstop.

The real backstops on ghost tracks are:

1. **Coherence** — the full-lap-span coherence check over the common ghost-arc axis, with the
   coherence and alignment thresholds **re-derived against the ghost-arc basis**, not the owner-tuned
   1 m/2 m envelope reused blind (`CenterlineCoherence` bins on the same axis and would otherwise
   inherit any misalignment and pass a bad bake).
2. **The Monza/Spa calibration gate** — the network-free B2 unit test that runs the detector on the
   owner Monza/Spa centerlines in two modes: G-intact + new channel must reproduce the owner-baked
   corner maps (no regression), and G=0 + new channel must recover the fast corners (tight corners all
   present, fast corners restored, count within N). W and `SustainedScale` are tuned against this
   oracle, and it fixes the numerical per-track acceptance for the 12.
3. **Corner-layout review** — authoring RU corner names strictly against the *baked* corner ids after
   detection (via `CornerGeometryReviewPage`), never from prior knowledge of the track, so a
   mislabelled or artifact corner is caught by a human rather than passing a count-only check.

A track that has no usable loop or whose derived line sits past the ceiling is **explicitly skipped and
listed in the PR** (OD-B2) rather than shipped.

## Consequences

- This ADR **does not change owner-baked Monza and Spa.** Their vendored `centerline.*.json` and
  `cornerGeometry.*.json` are untouched; the runtime loads the pre-baked JSON as before. The
  curvature-integral channel is a **dev-time bake** change to `CornerCenterlineDetector` only, gated so
  that G-intact detection still reproduces the existing owner maps.
- The two degenerate fields (`Trigger=Curvature`, `PeakLateralG=0`) are the on-disk signature of a
  ghost-derived corner map; any consumer that needs to distinguish provenance can read them.
- `LateralG=0` on ghost centerline bins is load-bearing: corner-type gating (ADR-0019) on these tracks
  relies on curvature geometry, not the (absent) lateral-load channel.
- The alien-line median-deviation guard remains a hard gate on owner-baked tracks and degrades to an
  informational log on ghost tracks; coherence + the calibration gate + corner-layout review carry the
  trust burden there.
