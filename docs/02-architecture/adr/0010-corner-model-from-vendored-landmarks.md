# ADR-0010: Corner model from a vendored landmark dataset; names at the prompt layer

**Status**: Accepted
**Date**: 2026-06-21

## Context

Phase 2 emits `CornerEvent`s keyed by `corner_id`, which means compute needs to know where each
corner is on every track (entry/apex/exit by `normalized_car_position`) and, for the driver-facing
voice in Phase 3, a human name ("Eau Rouge", not "turn 5").

Three sourcing options were considered:

1. **Hand-author a registry per track** — rejected: does not scale to "all tracks", and unwinding
   hand-authored data later is expensive.
2. **Derive corner geometry from the driver's own lap** (braking zones → corners) — works with zero
   authoring and is sim-agnostic, but conflates two independent concerns: corner *geometry* (where
   the corner physically is) and reference *performance* (the target to beat). Geometry derived from
   a slow/erratic driver's lap can place corner windows poorly, which degrades coaching — the exact
   failure that makes the product pointless.
3. **Vendor an existing dataset** — `CrewChiefV4`'s `trackLandmarksData.json` maps, per track, a list
   of `{ landmarkName, distanceRoundLapStart, distanceRoundLapEnd }` in metres round the lap.
   Convertible to `normalized_car_position` via lap length (`AccTrackCatalog`). **License: MIT**
   (Copyright (c) 2019-2022 Britton IT Ltd) — verified on the canonical repo
   `https://gitlab.com/mr_belowski/CrewChiefV4` (the GitHub `mrbelowski/CrewChiefV4` is a stale,
   migrated mirror without a project LICENSE, which is why GitHub's badge mis-detected a bundled
   dependency's notice). MIT permits redistribution provided the copyright + license notice are
   retained. ACC coverage is **partial** (Spa/Barcelona/Monza confirmed; uneven beyond), and its
   track keys are its own (`acTrackNames`) needing an alias map onto our normalized `track_id`.

Separately, ACC's own shared memory already provides authoritative sector structure
(`graphics.CurrentSectorIndex`, `static.SectorCount`) — no dataset needed for sectors.

## Decision

**Corner geometry comes from the MIT-licensed CrewChief landmark dataset (vendored with its notice);
sectors come from the sim; geometry derived from the driver's lap is a fallback only; corner names
live at the prompt layer, never in compute.**

### Track-model resolution (per `track_id`)

The **dataset** model resolves at session start. The **derived** model can only resolve *after the
first clean lap completes* (it consumes lap segmentation + compute kernels), so an uncovered track
starts at `none` and gains corners mid-session.

1. **Dataset model** — vendored landmark entry exists and ranges are sane
   (`0 ≤ start < end ≤ lapLength`): corners from landmarks (with names), `corner_id =
   <track_id>_<landmark-slug>`.
2. **Derived model** — no entry, or ranges fail the sanity check: corner windows derived from the
   driver's fastest **clean** lap (brake-on → min-speed → throttle-resume), `corner_id =
   <track_id>_t01..NN` positional, names `null`. Rebuilt idempotently when a faster clean lap arrives.
3. **Sectors** — always from the sim (`CurrentSectorIndex` transitions + `SectorCount`), independent
   of 1/2; works on every track.
4. **Neither yet** — no clean lap *and* not in the dataset: `CornerEvent`s are suppressed this
   session; `SectorEvent`/`LapEvent`/`SessionEvent` still emit. Corners appear once a clean lap
   derives them.

The resolved source (`dataset | derived | none`) is logged at session start so coverage gaps are
visible.

### Naming is a Phase-3 prompt concern, not compute

- Compute emits only the stable `corner_id` token. It never carries human corner names.
- The Coach `PromptBuilder` (Phase 3) injects a `track_id → {corner_id: name}` table (sourced from
  the same vendored landmark file) so the LLM speaks "Eau Rouge" to the driver. The LLM does not
  invent or persist names; it reads the supplied map.
- Fallback (derived) tracks have no names → the prompt falls back to positional phrasing ("turn 5").

## Why

- **Geometry decoupled from driver skill — on covered tracks**: corner windows for dataset-covered
  tracks come from a curated dataset, so a rookie's bad lap no longer corrupts where corners are.
  **On uncovered tracks the derive fallback still ties geometry to the driver's lap** — that residual
  risk is real (see Tradeoffs) and shrinks as dataset coverage grows. The reference-benchmark risk is
  a separate concern (C7 PB selection), which degrades gracefully (coach vs your own best).
- **All tracks, no authoring**: dataset covers the major tracks; the derive fallback covers the rest;
  sectors are free from the sim. Every track gets a model.
- **Licensing**: the CrewChief landmark file is MIT (Britton IT Ltd), so it is redistributable with
  its notice retained. The paid pro datasets (Coach Dave, Popometer) are under Blancpain/NDA terms
  and cannot be bundled.
- **Names out of compute**: keeps compute sim-agnostic and lets naming evolve (localization, new
  tracks) without touching the hot path. `corner_id` churn (e.g., a track later added to the dataset)
  is harmless — `CornerEvent` is transient (no corner table in `data-model.md`) and references are
  keyed by position grid, not `corner_id`.

## Tradeoffs

- Vendoring a third-party file means tracking upstream updates manually and carrying the MIT notice
  (copyright + license text) alongside the vendored data.
- Dataset ACC coverage is incomplete, so some tracks run on the (nameless, lap-derived) fallback —
  which re-introduces the driver-skill-corrupts-geometry risk on those tracks — until a landmark
  entry is added.
- Landmark ranges are in metres against CrewChief's lap-length assumption; a track-version mismatch
  can push a range out of `[0, lapLength]` — the sanity check drops the offending track to the
  fallback rather than emitting a misplaced corner.

## Consequences

- The MIT-licensed `trackLandmarksData.json` ships in the repo with its MIT notice (from
  `gitlab.com/mr_belowski/CrewChiefV4`); an alias map bridges the dataset's `acTrackNames` → our
  `track_id` (extends `AccTrackCatalog`).
- Phase 2 `C5` builds `TrackModelStore`/`TrackModelBuilder` around this resolution order (dataset
  primary, derive fallback), not pure derivation.
- Phase 3 `PromptBuilder` owns `corner_id → name` resolution from the same dataset.
