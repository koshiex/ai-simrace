# Telemetry Schema

The single source of truth lives at `src/SimCoach.Contracts/Schemas/telemetry.proto`. This document narrates it.

---

## 1. Messages

### `TelemetryFrame`
One frame emitted per adapter tick (target 100–333 Hz on ACC).

| Field | Type | Unit | Notes |
|---|---|---|---|
| `t` | `google.protobuf.Timestamp` | UTC | wall-clock at frame capture |
| `sim` | `string` | — | `"acc"`, `"iracing"`, `"lmu"`, `"f125"` |
| `track_id` | `string` | — | normalised (`"spa"`, `"monza"`) |
| `car_id` | `string` | — | normalised (`"audi_r8_lms_evo_ii"`) |
| `weather_bucket` | `string` | — | `"dry-cool"`, `"dry-warm"`, `"damp"`, `"wet"` |
| `lap_number` | `int32` | — | 1-based; NOT the lap-boundary trigger (ACC desync — see KB `acc-lap-boundary-timing`) |
| `lap_distance_m` | `float` | m | cumulative since start of lap |
| `normalized_car_position` | `float` | 0..1 | position along lap; a high→low wrap (≈1→0) is the start-line crossing |
| `speed_mps` | `float` | m/s | |
| `throttle_pct` | `float` | 0..1 | |
| `brake_pct` | `float` | 0..1 | |
| `clutch_pct` | `float` | 0..1 | |
| `steer_rad` | `float` | radians | left negative, right positive |
| `gear` | `int32` | — | 0=N, -1=R, 1..8 |
| `rpm` | `float` | rpm | |
| `tyre_temp_c` | `repeated float` | °C | [FL, FR, RL, RR] |
| `tyre_pressure_kpa` | `repeated float` | kPa | [FL, FR, RL, RR] |
| `tyre_wear_pct` | `repeated float` | 0..1 | [FL, FR, RL, RR] |
| `brake_temp_c` | `repeated float` | °C | [FL, FR, RL, RR] |
| `wheel_slip` | `repeated float` | — | [FL, FR, RL, RR] |
| `wheel_load_n` | `repeated float` | N | [FL, FR, RL, RR] |
| `suspension_travel_m` | `repeated float` | m | [FL, FR, RL, RR] |
| `g_force_g` | `Vec3` | g | x = lateral, y = vertical, z = longitudinal |
| `fuel_l` | `float` | L | remaining |
| `fuel_per_lap_l` | `float` | L | rolling average |
| `tc_active` | `bool` | — | |
| `abs_active` | `bool` | — | |
| `flags_active` | `int32` | — | bit flags (yellow, blue, green) |
| `air_temp_c` | `float` | °C | |
| `track_temp_c` | `float` | °C | |
| `wind_speed_mps` | `float` | m/s | |
| `world_pos` | `Vec3` | m | track-frame coordinates of the player car (Phase 2 compute input) |
| `current_sector_index` | `int32` | — | 0-based sector the car is in (Phase 2) |
| `sector_count` | `int32` | — | total sectors on the track (Phase 2) |
| `tyres_out` | `int32` | — | off-track wheel count (Phase 2) |
| `is_valid_lap` | `bool` | — | sim's own lap-validity flag (Phase 2) |

### `LapEvent`
Emitted at finish line.

| Field | Type | Notes |
|---|---|---|
| `t` | timestamp | |
| `lap_number` | int32 | |
| `lap_time_ms` | int32 | |
| `delta_ms` | int32 | vs reference (positive = slower) |
| `is_pb` | bool | |
| `is_clean` | bool | no off-tracks, no contact |
| `top_losses` | repeated `CornerLoss` | top 3 corners contributing to the delta |

### `SectorEvent`
Emitted at each sector cross.

| Field | Type | Notes |
|---|---|---|
| `t` | timestamp | |
| `sector_idx` | int32 | 0, 1, 2 |
| `sector_time_ms` | int32 | |
| `delta_ms` | int32 | vs reference sector |
| `top_losses` | repeated `CornerLoss` | top 3 corners in the sector |

### `CornerEvent`
Emitted at apex exit (throttle resumes ≥ 50%).

| Field | Type | Notes |
|---|---|---|
| `t` | timestamp | |
| `corner_id` | string | normalised (`"spa_t05_eau_rouge"`) |
| `delta_ms` | int32 | vs reference corner |
| `brake_point_diff_m` | float | negative = braked too early |
| `min_speed_diff_kmh` | float | negative = slower min speed |
| `trail_brake_pct_self` | float | 0..1 |
| `trail_brake_pct_ref` | float | 0..1 |
| `racing_line_deviation_m` | float | RMS distance to reference racing line |
| `off_track` | bool | |
| `understeer_score` | float | 0..1 |
| `oversteer_score` | float | 0..1 |

### `SessionEvent`
Emitted at session end.

| Field | Type | Notes |
|---|---|---|
| `t` | timestamp | session end |
| `session_id` | string | uuid |
| `lap_count` | int32 | |
| `clean_lap_count` | int32 | |
| `pb_time_ms` | int32 | |
| `average_lap_ms` | int32 | clean laps only |
| `stints` | repeated `StintSummary` | tyre stint summaries |
| `understeer_trend` | float | -1..1 (negative = oversteer, positive = understeer) |
| `tyre_degradation_pct` | repeated float | per stint |

---

## 2. Provenance per Sim

| Field | ACC source | iRacing source | LMU source | F1 25 source |
|---|---|---|---|---|
| `speed_mps` | `physics.speedKmh / 3.6` | `Speed` | `mVehicleScoring.mSpeed` | `m_speed / 3.6` |
| `throttle_pct` | `physics.gas` | `Throttle` | `mTelemetry.mUnfilteredThrottle` | `m_throttle` |
| `brake_pct` | `physics.brake` | `Brake` | `mTelemetry.mUnfilteredBrake` | `m_brake` |
| `delta_ms` | computed (no native channel) | `LapDeltaToSessionBestLap_ms` | computed | `m_deltaToCarInFrontInMS` / computed |
| `tyre_temp_c` | `physics.tyreCoreTemp[4]` | `LFtempCM`/`RFtempCM`/... | `mTelemetry.mWheels[4].mTemperature[3]` | `m_tyresInnerTemperature` |
| `world_pos` | `graphics.carCoordinates[slot]` where `slot = indexOf(carId, playerCarId)` — `playerCarId` is an id **value**, not a slot index | — | — | — |
| `current_sector_index` | `graphics.currentSectorIndex` | — | — | — |
| `sector_count` | `static.sectorCount` | — | — | — |
| `tyres_out` | `physics.numberOfTyresOut` — **"Not used in ACC" → always 0 live**; honest passthrough (real data only from other sims / synthesized fixtures). The clean-lap off-track gate must rely on `is_valid_lap` on ACC. | — | — | — |
| `is_valid_lap` | `graphics.isValidLap` (ACC `int`; `!= 0 → true`) | — | — | — |
| ... | | | | |

Full per-sim mapping in adapters' `*FrameMapper.cs`.

---

## 3. Versioning

- Proto v1 = MVP shape above.
- Add optional fields only; never reorder or repurpose field numbers.
- Major version bumps require ADR.
