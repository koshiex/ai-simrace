# Functional Requirements — SimCoach MVP

**Status**: Draft v1
**Scope**: MVP only (ACC, Russian, single user, local-first)

Format: FR-### functional, NFR-### non-functional. Each row traces to the module that owns it.

---

## 1. Telemetry & Sim Integration

| ID | Requirement | Module |
|---|---|---|
| FR-001 | Read ACC shared memory at ≥ 100 Hz from `Local\acpmf_physics`, `Local\acpmf_graphics`, `Local\acpmf_static`. | Adapters.ACC |
| FR-002 | Detect session start/end, lap start/end, sector boundaries, current car/track/weather, on/off-track, contact. | Pipeline.Compute |
| FR-003 | Survive sim restart, alt-tab, and config reload without crashing the app. Reconnect within 5 s of game becoming available again. | Adapters.ACC |
| FR-004 | Persist every session as MCAP under `%LOCALAPPDATA%/SimCoach/sessions/<ts>/raw.mcap`. Rotated every 60 s, zstd-compressed. | Storage.MCAP |
| FR-005 | Provide a replay tool that re-emits an MCAP file in real time for testing without needing the game running. | Storage.MCAP, dev tool |

## 2. Reference Lap Management

| ID | Requirement | Module |
|---|---|---|
| FR-010 | On lap completion, evaluate vs. existing PB matched on `(trackId, carId, weatherBucket)`; replace PB if faster and clean (no off-tracks, no contact). | Reference.Store |
| FR-011 | Store reference lap as Parquet, channels resampled to 1 sample per 1 m of `normalizedCarPosition`. | Reference.Store |
| FR-012 | Settings UI lets the user browse, delete, pin, and import/export reference laps. | App / Overlay |
| FR-013 | "Pin" prevents auto-replacement when a faster but pinned-comparison lap should remain. | Reference.Store |
| FR-014 | If no reference is available for the current `(track, car, weather)` triple, fall back to "best of session so far" and label tips accordingly ("no PB yet"). | Coach.Engine |

## 3. Compute / Derivations

| ID | Requirement | Module |
|---|---|---|
| FR-020 | Per-frame: compute braking-on/off events, peak brake pressure, trail-brake percentage, throttle-on event, min-speed in corner, steering smoothness. | Pipeline.Compute |
| FR-021 | Per-corner: emit `CornerEvent` at apex exit with `delta_ms` vs reference, `brake_point_diff_m`, `min_speed_diff_kmh`, `trail_brake_pct_self/ref`, `racing_line_deviation_m`. | Pipeline.Compute |
| FR-022 | Per-sector: emit `SectorEvent` at sector cross with `sector_idx`, `delta_ms`, top 3 corner losses for the sector. | Pipeline.Compute |
| FR-023 | Per-lap: emit `LapEvent` at finish line with `lap_time_ms`, `delta_ms`, `is_pb`, `clean`, top 3 corner losses. | Pipeline.Compute |
| FR-024 | Session aggregate: `SessionEvent` emitted at session end with stints, lap distribution, best/worst sectors, understeer/oversteer trend. | Pipeline.Compute |

## 4. Coaching Engine

| ID | Requirement | Module |
|---|---|---|
| FR-030 | Build "Gold" artifact JSON (200–500 tokens) per event from compute output. | Coach.GoldArtifactBuilder |
| FR-031 | Call OpenRouter with `response_format: json_schema, strict: true`. | LLM.OpenRouterClient |
| FR-032 | LLM selects from bounded `ActionRegistry` (`brake_later_by_meters`, `tighten_apex`, `lift_partial_throttle`, `more_trail_brake`, …). No free-form prose. | Coach.ActionRegistry |
| FR-033 | Russian phrase length: ≤ 8 words for in-corner, ≤ 25 words for sector/lap, ≤ 200 words for post-session. | Coach.PromptBuilder |
| FR-034 | If LLM response fails schema or times out, fall back to pre-baked RU template for the chosen action. | Coach.Engine |
| FR-035 | Rule engine suppresses voice during active braking zone, apex window, recent contact, recent off-track, or user-set quiet zones. | Coach.RuleEngine |
| FR-036 | Track LLM input/output tokens per call; persist to SQLite `llm_usage`. | LLM.CostMeter |
| FR-037 | Circuit breaker opens after 3 consecutive LLM failures in 60 s; closes after 60 s cool-down; surfaces UI banner. | LLM.CircuitBreaker |

## 5. Voice & Audio

| ID | Requirement | Module |
|---|---|---|
| FR-040 | TTS first-audio-frame ≤ 200 ms after the LLM response is parsed. | Voice |
| FR-041 | Streaming chunked PCM (20–40 ms frames). | Voice |
| FR-042 | Priority queue with preemption depth = 1 in-flight + 1 queued. Preempt with 10–20 ms linear fade-out to avoid clicks. | Audio.PriorityAudioQueue |
| FR-043 | Stale tips drop on cancel: ≥ 1 s old for corner-critical, ≥ 2 s old for general. | Audio |
| FR-044 | Hotkey toggles mute (default `Ctrl+Alt+M`); persisted. | App / Audio |
| FR-045 | Volume independent of game volume. | Audio |
| FR-046 | Optional Yandex SpeechKit backend selectable in settings. | Voice |

## 6. Overlay

| ID | Requirement | Module |
|---|---|---|
| FR-050 | Transparent topmost Avalonia window over the game. Click-through (Win32 `WS_EX_TRANSPARENT`). | Overlay |
| FR-051 | Shows: live delta-to-reference bar, three sector bars (S1/S2/S3), current tip text (≤ 80 chars), lap counter. | Overlay |
| FR-052 | Position, opacity, font size, visibility of each element persisted per user. | Overlay |
| FR-053 | "Race mode" toggle hides everything except the delta bar. | Overlay |
| FR-054 | Refresh rate cap 30 Hz; never block the telemetry pipeline. | Overlay |
| FR-055 | When the game window loses focus, overlay hides automatically. | Overlay |

## 7. Post-Session Debrief

| ID | Requirement | Module |
|---|---|---|
| FR-060 | At session end, build a session-level Gold artifact (sector dist, top time losses, top improvement area, U/O trend, fuel/tyre summary). | Coach.GoldArtifactBuilder |
| FR-061 | Call OpenRouter `deepseek/deepseek-chat-v3.2` (or user-selected debrief model) with longer prompt. | LLM.OpenRouterClient |
| FR-062 | Debrief window shows: per-sector chart, brake/throttle/steering traces overlaid on reference, TTS playback, written debrief, action checklist for next session. | App |
| FR-063 | Export debrief to PDF or Markdown on demand. | App |

## 8. Settings & UX

| ID | Requirement | Module |
|---|---|---|
| FR-070 | First-run wizard: detect ACC, point at telemetry folder, set OpenRouter API key, choose voice (Silero / Yandex), tour overlay. | App |
| FR-071 | Settings UI panels: General, Telemetry, Voice, LLM, Overlay, References, Hotkeys, Privacy, About. | App |
| FR-072 | Cost meter UI panel showing per-session and rolling-30-day LLM spend; alert when monthly cap is exceeded. | App |
| FR-073 | All settings live in `%APPDATA%/SimCoach/appsettings.json`; secrets live in `%APPDATA%/SimCoach/secrets.json` (excluded from any export/share feature). | App |

---

## Non-Functional Requirements

| ID | Requirement |
|---|---|
| NFR-001 | Overlay frame budget < 2 ms at 60 Hz refresh on a mid-range GPU. |
| NFR-002 | Process RAM ≤ 600 MB steady state during a race. |
| NFR-003 | No DLL injection, no kernel hooks, no game-process modifications. Pass EAC smell test. |
| NFR-004 | All user data stays local. Only Gold-tier JSON (200–500 tokens) leaves the machine, only to the user-chosen LLM and optional cloud TTS. |
| NFR-005 | Single-binary self-contained Windows installer ≤ 200 MB. |
| NFR-006 | Crash-free session rate ≥ 95% in private beta, ≥ 99% in public release. |
| NFR-007 | LLM cost per 30-min session ≤ $0.05 with default models. |
| NFR-008 | All long-running services are `IHostedService`; graceful shutdown drains channels and flushes MCAP. |
| NFR-009 | Code coverage ≥ 80% for `Pipeline.*`, `Coach.*`, `Reference.*` modules; ≥ 50% for `Overlay.*`. |
| NFR-010 | All Russian text in the codebase lives in `Resources.*.resx` for future localisation. |

---

## Traceability

Each FR maps to a module under `src/`. CI verifies that the module hosting an FR exposes a public type whose XML doc references the FR ID (lightweight contract).
