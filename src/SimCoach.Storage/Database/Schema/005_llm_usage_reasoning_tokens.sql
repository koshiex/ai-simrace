-- 005: reasoning-token accounting on llm_usage (Phase 3 M28). Mirrors 002's additive ALTER.
-- The provider already reads reasoning_tokens and cost already bills them at the output rate; this column
-- closes the observability hole so "thinking is off" (Reasoning:Off routes → 0) is confirmable from data.
ALTER TABLE llm_usage ADD COLUMN reasoning_tokens INTEGER NOT NULL DEFAULT 0;
