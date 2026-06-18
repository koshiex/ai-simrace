# ADR-0008: Record only LIVE, identified telemetry frames

**Status**: Accepted
**Date**: 2026-06-18

## Context

The acquisition loop (`AccSharedMemoryReader`) emitted a `TelemetryFrame` for every new
physics `packetId`, regardless of session state. The first real recording (session
`20260617-224417-919`, BMW M4 GT3 @ Spa — issues #1 and #2) showed two consequences:

1. While the car is dormant in the box before/around going live, ACC has not populated the
   static page, so frames carry **empty `track_id`/`car_id`** and zeroed sensors. The first
   386 frames of `segment-0001` were engine-off box frames; `segment-0000` was a single
   identity-empty connect-time frame. Phase 2 compute and reference-lap lookup are keyed on
   `track_id`/`car_id` and cannot use identity-less frames.
2. Environment temps populate late: `roadTemp`/`airTemp` stayed `0` for ~21 s even after the
   session was live. `AccFrameMapper.DeriveWeatherBucket` classified any `roadTemp < 25 °C`
   as `dry-cool`, so ~36 % of `segment-0001` got a spurious `dry-cool` bucket in a dry-warm
   session.

A real graphics dump (`acc_graphics.bin`) shows a car parked in the box already reports
`Status == 2 (AC_LIVE)`. So gating on `Status == LIVE` **alone** would not drop the
empty-identity box frames — identity must be checked too.

## Decision

**The ACC reader records a frame only when ACC is LIVE and static identity is populated.**

- `AccFrameMapper.IsRecordable(snapshot)` returns true only when
  `graphics.Status == AC_LIVE (2)` **and** normalized `track_id` and `car_id` are both
  non-empty. The reader evaluates it before mapping; rejected frames never enter the channel,
  pipeline, or recorder.
- `IsInPit` is **not** gated — pit-lane frames during out/in-laps are valid driving telemetry.
- Independently, `DeriveWeatherBucket` treats `roadTemp <= 0` as "no data" and falls to the
  dry-warm branch instead of misclassifying it as dry-cool — a defensive guard that holds even
  for the LIVE-but-temps-not-ready window.

## Why

- **Downstream correctness**: every recorded frame is keyable by track/car, which is what
  Phase 2 compute and reference-lap selection require.
- **Identity, not just status**: ACC reports LIVE while parked, so the identity check is what
  actually removes the box frames the status check misses.
- **Drop at the source**: gating before mapping saves pipeline bandwidth and keeps the recorder,
  fan-out, and ingest service unchanged.
- **Weather guard is orthogonal**: even correctly-gated LIVE frames have `roadTemp == 0` early,
  so the no-data guard is worth keeping regardless of the liveness gate.

## Tradeoffs

- A few legitimate frames at the very start of a live stint (before the static page refreshes,
  ≤ ~1 s) are dropped. Acceptable: they precede any meaningful driving.
- The gate is always-on with no config toggle (KISS) — recording dormant frames is never useful.
  A toggle can be added later if a debugging need appears.
- Gating is ACC-specific (AC_STATUS is an ACC concept); other sims will need their own
  liveness predicate when added.

## Consequences

- `AccSharedMemoryReader` takes an injected `Func<AccTelemetrySnapshot, bool> shouldRecord`;
  production wires `AccFrameMapper.IsRecordable` (`TelemetryComposition.AddAccSource`).
- No change to `McapRecorderService`, `IngestService`, `TelemetryFanOut`, or `RecordingOptions`.
- Closes issues #1 and #2.
