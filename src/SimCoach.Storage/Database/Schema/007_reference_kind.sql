-- Migration 007 — reference-kind taxonomy (ADR-0021). Rebuilds [references] to (a) add a `kind`
-- discriminator that participates in the uniqueness key and (b) make `parquet_path` nullable for the
-- row-only `optimal` kind. A UNIQUE-key change and a NOT-NULL -> NULL relaxation cannot be ALTERed, so
-- this is the SQLite table-rebuild procedure.
--
-- No BEGIN/COMMIT here: DatabaseMigrator owns the transaction. PRAGMA foreign_keys cannot be toggled
-- inside a transaction (it is a no-op there) and nothing FK-references [references].id, so the rebuild
-- needs no toggle: dropping/recreating [references] cascades to no child rows.

CREATE TABLE references_new (
  id TEXT PRIMARY KEY,
  track_id TEXT NOT NULL,
  car_id TEXT NOT NULL,
  weather_bucket TEXT NOT NULL,
  source_session_id TEXT REFERENCES sessions(id) ON DELETE SET NULL,
  source_lap_number INTEGER,
  lap_time_ms INTEGER NOT NULL,
  parquet_path TEXT,
  pinned INTEGER NOT NULL DEFAULT 0,
  created_at_utc TEXT NOT NULL,
  kind TEXT NOT NULL DEFAULT 'pb',
  optimal_sector_ms TEXT,
  sector_sources_json TEXT,
  UNIQUE(track_id, car_id, weather_bucket, kind),
  CHECK (kind = 'optimal' OR parquet_path IS NOT NULL),
  CHECK (kind <> 'optimal' OR optimal_sector_ms IS NOT NULL)
);

-- Every existing row is a PB. Stamp kind='pb' and leave the optimal-only columns NULL.
INSERT INTO references_new
  (id, track_id, car_id, weather_bucket, source_session_id, source_lap_number,
   lap_time_ms, parquet_path, pinned, created_at_utc, kind, optimal_sector_ms, sector_sources_json)
SELECT
  id, track_id, car_id, weather_bucket, source_session_id, source_lap_number,
  lap_time_ms, parquet_path, pinned, created_at_utc, 'pb', NULL, NULL
FROM [references];

DROP TABLE [references];

ALTER TABLE references_new RENAME TO [references];

-- Defensive integrity guard: abort the migration if the rebuild left any broken FK on [references].
-- RAISE() is trigger-only in SQLite, so a temp table whose CHECK fails on a non-zero violation count
-- stands in as the abort mechanism (the failing INSERT rolls back the migrator transaction). Nothing
-- FK-references references.id, so pragma_foreign_key_check is normally empty and this is a no-op.
CREATE TEMP TABLE _fk_guard_007 (violations INTEGER CHECK (violations = 0));
INSERT INTO _fk_guard_007 (violations) SELECT count(*) FROM pragma_foreign_key_check('references');
DROP TABLE _fk_guard_007;
