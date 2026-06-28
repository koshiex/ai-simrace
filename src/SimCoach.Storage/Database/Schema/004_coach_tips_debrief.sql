-- 004: reserved debrief columns on coach_tips (Phase 3 PR-H / D9).
-- The session-cadence (debrief) tip row carries these; they are written by the P6 debrief-delivery
-- path. Declared nullable now so P6/P7 do not migrate against live data (003 is immutable post-merge).
-- top_losses_json is the structured per-corner loss attribution that powers the debrief headline.
ALTER TABLE coach_tips ADD COLUMN top_losses_json TEXT;
ALTER TABLE coach_tips ADD COLUMN debrief_prose TEXT;
ALTER TABLE coach_tips ADD COLUMN setup_hint TEXT;
ALTER TABLE coach_tips ADD COLUMN checklist_json TEXT;
ALTER TABLE coach_tips ADD COLUMN per_sector_deltas_json TEXT;
ALTER TABLE coach_tips ADD COLUMN balance_verdict TEXT;
ALTER TABLE coach_tips ADD COLUMN audio_artifact_ref TEXT;
