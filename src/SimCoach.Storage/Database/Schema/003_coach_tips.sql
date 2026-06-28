-- 003: coach_tips — one row per emitted coaching tip (Phase 3 PR-G / D8).
-- Pulled into PR-G (was PR-H) so CoachTipRepository/ConsoleTipSink test on a real migrated table.
-- The reserved debrief columns (top_losses_json, …) arrive in 004 (PR-H); migrations are immutable post-merge.
CREATE TABLE coach_tips (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  session_id TEXT NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
  cadence TEXT NOT NULL,
  corner_id TEXT,
  lap_number INTEGER,
  action_id TEXT NOT NULL,
  action_label_short TEXT,
  rendered_param TEXT,
  priority_phase TEXT NOT NULL,
  priority_rank INTEGER NOT NULL,
  severity TEXT NOT NULL,
  phrase_ru TEXT NOT NULL,
  corner_name TEXT,
  source TEXT NOT NULL,
  no_pb_yet INTEGER NOT NULL DEFAULT 0,
  provider_model_id TEXT,
  generated_at_utc TEXT NOT NULL
);

CREATE INDEX idx_coach_tips_session ON coach_tips(session_id);
