-- Migration 001 — initial schema. Source of truth: docs/04-data/data-model.md.
-- No BEGIN/COMMIT here: DatabaseMigrator owns the transaction.

CREATE TABLE sessions (
  id TEXT PRIMARY KEY,
  started_at_utc TEXT NOT NULL,
  ended_at_utc TEXT,
  sim TEXT NOT NULL,
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
  raw_offset_in_mcap INTEGER,
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
  UNIQUE(track_id, car_id, weather_bucket)
);

CREATE TABLE llm_usage (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT REFERENCES sessions(id) ON DELETE SET NULL,
  ts_utc TEXT NOT NULL,
  model_id TEXT NOT NULL,
  cadence TEXT NOT NULL,
  input_tokens INTEGER NOT NULL,
  output_tokens INTEGER NOT NULL,
  cost_usd REAL NOT NULL,
  latency_ms INTEGER NOT NULL,
  status TEXT NOT NULL
);

CREATE TABLE settings (
  key TEXT PRIMARY KEY,
  value TEXT NOT NULL,
  updated_at_utc TEXT NOT NULL
);

CREATE INDEX idx_laps_session ON laps(session_id);
CREATE INDEX idx_llm_usage_ts ON llm_usage(ts_utc);
CREATE INDEX idx_sessions_track_car ON sessions(track_id, car_id);
