# ADR-0004: OpenRouter for LLM, Gemini 2.5 Flash + DeepSeek V3.2 default models

**Status**: Accepted
**Date**: 2026-06-01

## Context

User insisted on cloud-only LLM (game uses the local GPU). User asked explicitly for cheaper models — flagged that Sonnet 4.6 / Haiku 4.5 would "стоить ужасно дорого" at session-level chatter rates.

OpenRouter aggregates many providers under one API, supports streaming SSE, and supports schema-enforced `response_format`. We pay OpenRouter's ~5% passthrough.

## Decision

Default models (configurable in settings):

| Cadence | Model (OpenRouter ID) | $/M in | $/M out |
|---|---|---|---|
| In-corner / sector / lap (real-time) | `google/gemini-2.5-flash` | $0.30 | $2.50 |
| Post-session debrief | `deepseek/deepseek-chat-v3.2` | $0.14 | $0.28 |

Premium opt-in (Pro+ tier):
- Real-time: `anthropic/claude-haiku-4.5` ($1.00 / $5.00)
- Debrief: `anthropic/claude-sonnet-4.6` ($3.00 / $15.00)

User originally suggested `gemini-3.5-flash` (≈$1.50 / $9 on OpenRouter mid-2026) but that's pricier than Haiku 4.5. We default to **2.5 Flash** as the cheaper, fast Gemini; user can switch in settings.

## Why

- **Cost-first**: a 30-min session generates ~50 LLM calls × 500-token Gold artifact ≈ 25k input + 5k output tokens. At Gemini 2.5 Flash that's ~$0.02 per session. Well under user's expectation.
- **Latency**: Gemini 2.5 Flash hits ~170 tokens/s and ~1 s TTFT — fast enough for in-corner.
- **Quality**: Russian output is acceptable on both models. We test against held-out fixtures before each release.
- **Configurability**: model IDs live in settings; user can swap any time without code changes.

## Pre-LLM determinism

We **never** send raw telemetry to the LLM. We pre-compute deterministic deltas in C# (brake point, min-speed, trail-brake %, off-track, line deviation) and send only the "Gold" artifact (200–500 token JSON) plus a registry of allowed action IDs.

The LLM picks an `action_id` and generates an ≤8-word Russian phrase. The voice template fills in if the LLM fails schema validation.

## Tradeoffs

- DeepSeek's Russian is mid-tier vs Claude's. We hedge by running a small RU quality eval on every release.
- Gemini 2.5 Flash has occasional schema-violation rate (~1–2%). We retry once with a stricter "you must output strict JSON matching schema" reminder before falling back to template.
- OpenRouter passthrough is a single point of failure. Circuit breaker + offline templates mitigate.

## Consequences

- `LLM.OpenRouterClient` uses `response_format: json_schema, strict: true`.
- Cost meter persists to SQLite; UI shows per-session and rolling 30-day spend.
- Settings UI exposes model picker with cost estimates per cadence.

## Addendum — 2026-07-05 (P3 doc reconciliation, M42)

Append-only; the 2026-06-01 Decision above is retained as dated history. Shipped routing (`appsettings.json`) as of P3 diverged from the original defaults:

| Cadence | Shipped model (OpenRouter slug) | Note |
|---|---|---|
| corner (real-time) | `google/gemini-3.1-flash-lite` | swapped from 2.5 Flash for tail-latency at the 2 s cap (M14) |
| sector / lap / strategy | `google/gemini-2.5-flash-lite` | |
| debrief | `anthropic/claude-sonnet-4.6` (`Reasoning: Low`) | promoted from premium opt-in to default; fallback `anthropic/claude-haiku-4.5` (`debrief_fallback` route) |

- **DeepSeek is not registered.** The original default `deepseek/deepseek-chat-v3.2` has no route entry in `appsettings.json` — it is config-gated *by absence*, not by a flag. FR-061 updated to match.
- **Slug, not canonical id.** The debrief model is the OpenRouter slug `anthropic/claude-sonnet-4.6` (with the dot). Do **not** "correct" it to the canonical Anthropic id `claude-sonnet-4-6` — that 404s on OpenRouter and breaks `FamilyOf`.
- **Rationale for the debrief promotion:** RU quality on the long (≤ 200-word) post-session summary outweighs the per-call cost (one debrief per session), and cached-input pricing (`CachedInputPerMillion`) already sits in the cost table.
