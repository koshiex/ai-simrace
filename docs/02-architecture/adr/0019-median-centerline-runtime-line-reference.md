# ADR-0019: Median centerline as the runtime LINE reference; corner-type gating stays compute-side

**Status**: Accepted
**Date**: 2026-07-05

## Context

Every reference-relative line signal — the unsigned RMS `racing_line_deviation_m` (field 9) and the
signed per-phase deviations M34 added (fields 18/19/20) — is currently measured against the driver's own
**PB lap**. That conflates two different references:

- a **TIME reference** — "where were you faster/slower" — which is legitimately the driver's own best lap
  (`delta_ms`, brake-point, min-speed diffs), and
- a **LINE reference** — "is your line wide/tight vs the ideal" — which, when it is the driver's own PB,
  makes a slow-but-consistent driver's line deviation collapse to ~0. They already drive that line every
  lap, so the coach goes quiet on line even though there is a better line to take. This is the core P3
  gap (recorded in the master backlog).

The bake already builds an **aggregate median centerline** (`MedianCenterlineBuilder.Build` →
`MedianCenterline` of `CenterlineBin[]`, ADR-0014) — the median world path over many clean laps, per
1-metre bin — and `CornerCenterlineDetector` differentiates it to detect corners + their apex radius.
But that geometry lives **offline** (the bake tool); nothing loads it at runtime.

## Decision

Split the LINE reference from the TIME reference at runtime:

- The **LINE reference** for the M34 signed deviations (and the RMS) becomes the **median centerline**,
  loaded at runtime from a vendored, schema-versioned, length-pinned asset
  `centerline.<trackId>.json` — serialized from the SAME `MedianCenterline` the bake already computes
  (no second derivation). It ships as an embedded resource alongside `cornerGeometry.<trackId>.json`.
- The **TIME reference** stays the driver's PB (`ReferenceLookup`) — `delta_ms` and the time-diffs are
  unchanged.
- **PB fallback:** when no vendored centerline exists for a track (or it is not length-consistent), the
  line deviations fall back to the PB world path exactly as today — the change is graceful and never
  worse than the status quo.
- **Corner-type gating stays compute-side.** Whether line-shape coaching is meaningful for a corner
  (a genuine turn vs a flat/kink) is decided in compute from the centerline's own geometry — the apex
  radius / lateral-load channel already on `MedianCenterline`/`CenterlineBin` — NOT by putting a
  `corner_radius_m` on the wire for the LLM to reason about. **Rejected alternative:** exposing
  `corner_radius_m` as a new `CornerEvent` field. The LLM is a selector+phraser (hard rule); a
  corner-type decision it cannot verify does not belong on the contract. Neutralising an ambiguous
  corner's signed deviation to 0 (already done geometrically in the M34 kernel) is refined by this
  compute-side gate; nothing new crosses the wire.

## Why

- **Fixes the core P3 gap.** A slow-consistent driver now sees line deviations vs the *ideal* corridor,
  not vs their own repeated line — the coach can finally say "take a wider/tighter line here".
- **Reuses what exists.** The centerline is already built during bake; M38 only *serialises* it and adds
  a runtime loader (the `CornerGeometryDocument`/`Dataset` pattern) — not a parallel centerline model.
- **No contract growth.** The LINE-vs-TIME split and the corner-type gate are entirely compute-side; the
  proto is untouched (the M34 fields 18/19/20 already carry the signal).
- **Safe rollout.** PB fallback means a track without a vendored centerline behaves exactly as before.

## Tradeoffs

- **Vendored-asset drift.** The `centerline.<trackId>.json` is length-pinned + schema-versioned; a
  geometry change requires a re-bake. A mismatched/absent asset degrades to PB fallback, not a crash.
- **Trust threshold.** The median is only trustworthy at `MedianCenterlineBuilder.MinLapsForTrust`
  clean laps (ADR-0014); below that the asset is not emitted and the runtime falls back to PB.
- **Coordinate parity.** The centerline bins are per-metre in world X/Z; the PB `ResampledLap` is a 0..1
  grid. The runtime deviation must sample both consistently by normalized position.

## Consequences

- `tools/SimCoach.Bake` serialises the existing `MedianCenterline` to `centerline.<trackId>.json`
  (M38-bake).
- The runtime `Corner` model carries the apex radius + channel the bake already produces but currently
  drops (M38-cornermodel).
- New `CenterlineGeometryDocument` / `CenterlineGeometryDataset` / `CenterlineStore` add only the
  persisted-document + embedded-resource loader layer (M38-store), mirroring `CornerGeometry*`.
- `CornerEventBuilder` measures the line deviations against the centerline with PB fallback (M38-linedev)
  — this changes shipping line math, so it carries the M43-gate ground-truth precondition — and gates the
  signed fields by corner type (M38-gate). `LineRelevanceMaxRadiusM` lands in `ComputeOptions` (Tier-2).
