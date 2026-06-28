-- 002: cost-meter columns on llm_usage (Phase 3 PR-F / D6).
-- model_id already exists in 001 (re-adding it would throw "duplicate column name").
ALTER TABLE llm_usage ADD COLUMN provider TEXT;
ALTER TABLE llm_usage ADD COLUMN cached_input_tokens INTEGER NOT NULL DEFAULT 0;
