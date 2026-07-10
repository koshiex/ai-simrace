-- Migration 006 — reference snapshot history (ADR-0017). Append-only record of every PB parquet ever
-- written for a (track, car, weather) triple, so past deltas stay auditable and reproducible. The
-- [references] table remains the single active pointer per triple; this is the versioned history behind it.
-- No BEGIN/COMMIT here: DatabaseMigrator owns the transaction.

CREATE TABLE reference_snapshots (
  id TEXT PRIMARY KEY,
  track_id TEXT NOT NULL,
  car_id TEXT NOT NULL,
  weather_bucket TEXT NOT NULL,
  source_session_id TEXT REFERENCES sessions(id) ON DELETE SET NULL,
  source_lap_number INTEGER,
  lap_time_ms INTEGER NOT NULL,
  parquet_path TEXT NOT NULL,
  created_at_utc TEXT NOT NULL
);

CREATE INDEX idx_reference_snapshots_triple
  ON reference_snapshots (track_id, car_id, weather_bucket, created_at_utc);
