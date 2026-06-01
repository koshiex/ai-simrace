# ADR-0006: Single Protobuf schema, sim-agnostic

**Status**: Accepted
**Date**: 2026-06-01

## Context

MVP is ACC-only, but the roadmap adds iRacing, LMU, F1 25 in phase 2. Each sim has a different native telemetry shape and update rate. If the pipeline depends on sim-specific structures, adding sim #2 is a rewrite.

## Decision

A **single** Protobuf schema (`SimCoach.Contracts.TelemetryFrame` + `LapEvent`, `SectorEvent`, `CornerEvent`, `SessionMeta`) is the only data type that flows through Pipeline, Storage, Reference, and Coach. Each `Adapters.<Sim>` is the only place that knows the sim's native shape; it translates into the common schema.

## Why

- **Pipeline stays sim-agnostic** — adding sim #2 means a new adapter + a release flag, nothing else.
- **MCAP recordings are interoperable** between sims for the analytics view ("show me my fastest Spa lap in ACC vs LMU").
- **Reference store** can match by `(trackId, carId, weatherBucket)` regardless of sim — keys are normalised in the adapter.
- **LLM prompt** is identical across sims.

## Schema highlights

```
message TelemetryFrame {
  google.protobuf.Timestamp t = 1;
  string sim = 2;                   // "acc", "iracing", "lmu", "f125"
  string track_id = 3;
  string car_id = 4;
  string weather_bucket = 5;        // "dry-cool", "dry-warm", "damp", "wet"
  float speed_mps = 6;
  float throttle_pct = 7;
  float brake_pct = 8;
  float steer_rad = 9;
  int32 gear = 10;
  float rpm = 11;
  float normalized_car_position = 12;  // 0..1 along lap
  repeated float tyre_temp_c = 13;     // [FL, FR, RL, RR]
  repeated float tyre_pressure_kpa = 14;
  repeated float brake_temp_c = 15;
  Vec3 g_force_g = 16;
  ...
}
```

Per-sim quirks (e.g., F1 25's `m_packetFormat`, iRacing's `LapDeltaTo*Lap`, LMU's plugin presence) live in the adapter; never leak past the adapter boundary.

## Tradeoffs

- Some sim-specific richness is lost (e.g., F1 25's ERS deployment mode). We add optional fields when needed; never required.
- Schema migrations require version bumps. Protobuf's wire format tolerates forward/backward additions of optional fields.

## Consequences

- `SimCoach.Contracts/Schemas/telemetry.proto` is the single source of truth.
- All adapters depend only on Contracts.
- Schema changes go through ADR review.
