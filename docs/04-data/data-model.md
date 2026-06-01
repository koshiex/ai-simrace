# Data Model — SimCoach

---

## SQLite Schema (`%LOCALAPPDATA%/SimCoach/simcoach.db`)

```sql
CREATE TABLE sessions (
  id TEXT PRIMARY KEY,             -- uuid
  started_at_utc TEXT NOT NULL,
  ended_at_utc TEXT,
  sim TEXT NOT NULL,               -- 'acc', 'iracing', 'lmu', 'f125'
  track_id TEXT NOT NULL,
  car_id TEXT NOT NULL,
  weather_bucket TEXT NOT NULL,
  lap_count INTEGER NOT NULL DEFAULT 0,
  clean_lap_count INTEGER NOT NULL DEFAULT 0,
  pb_time_ms INTEGER,
  mcap_path TEXT NOT NULL,
  parquet_path TEXT,
  notes TEXT
);

CREATE TABLE laps (
  id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
  lap_number INTEGER NOT NULL,
  lap_time_ms INTEGER NOT NULL,
  delta_vs_reference_ms INTEGER,
  is_pb INTEGER NOT NULL DEFAULT 0,
  is_clean INTEGER NOT NULL DEFAULT 0,
  s1_ms INTEGER,
  s2_ms INTEGER,
  s3_ms INTEGER,
  raw_offset_in_mcap INTEGER,      -- byte offset for fast seek
  UNIQUE(session_id, lap_number)
);

CREATE TABLE [references] (
  id TEXT PRIMARY KEY,
  track_id TEXT NOT NULL,
  car_id TEXT NOT NULL,
  weather_bucket TEXT NOT NULL,
  source_session_id TEXT REFERENCES sessions(id) ON DELETE SET NULL,
  source_lap_number INTEGER,
  lap_time_ms INTEGER NOT NULL,
  parquet_path TEXT NOT NULL,
  pinned INTEGER NOT NULL DEFAULT 0,
  created_at_utc TEXT NOT NULL,
  UNIQUE(track_id, car_id, weather_bucket)  -- only one PB per triple (unless pinned)
);

CREATE TABLE llm_usage (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT REFERENCES sessions(id) ON DELETE SET NULL,
  ts_utc TEXT NOT NULL,
  model_id TEXT NOT NULL,          -- e.g., 'google/gemini-2.5-flash'
  cadence TEXT NOT NULL,           -- 'corner', 'sector', 'lap', 'session'
  input_tokens INTEGER NOT NULL,
  output_tokens INTEGER NOT NULL,
  cost_usd REAL NOT NULL,
  latency_ms INTEGER NOT NULL,
  status TEXT NOT NULL             -- 'ok', 'schema_fail', 'timeout', 'error'
);

CREATE TABLE settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE INDEX idx_laps_session ON laps(session_id);
CREATE INDEX idx_llm_usage_ts ON llm_usage(ts_utc);
CREATE INDEX idx_sessions_track_car ON sessions(track_id, car_id);
```

---

## Parquet Partition Layout

```
%LOCALAPPDATA%/SimCoach/
├── sessions/
│   └── 2026-06-01_19-30-42/
│       ├── raw.mcap                    # rotating 60s segments concatenated
│       ├── laps.parquet                # all laps, one row group per lap
│       └── debrief.md                  # post-session export
└── references/
    └── spa_audi_r8_lms_evo_ii_dry-warm.parquet
```

`laps.parquet` schema:
- `lap_number: int32`
- `t_ms_from_lap_start: int32`
- `position_normalized: float`
- `speed_mps: float`
- `throttle_pct: float`
- `brake_pct: float`
- `steer_rad: float`
- `gear: int32`
- `tyre_temp_*: float` × 4
- `g_lat: float`, `g_long: float`
- ... (matches `TelemetryFrame` flat subset)

Reference parquet is the same schema but already resampled to 1 sample per 1 m of `normalizedCarPosition` for fast delta computation.

---

## File Lifecycle

| File | Created | Deleted |
|---|---|---|
| `raw.mcap` | Session start, rotated every 60 s | User deletes session, or scheduled cleanup (>90 days configurable) |
| `laps.parquet` | Session end (async conversion from MCAP) | Same as session delete |
| `references/*.parquet` | When a new PB is set | User deletes reference or session-source deleted |
| `simcoach.db` | App first run | "Delete all data" setting |
| `logs/*.log` | App start | 7-day rolling retention |
