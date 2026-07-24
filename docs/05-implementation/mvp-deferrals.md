# MVP Deferrals — what moves out of the MVP to later

Living backlog of scope intentionally **deferred from the MVP** (ACC driving-coach, phases 0–5),
with the *seam* we reserve now so picking it up later is additive, not a re-architecture.

MVP focus = **driving-technique coaching** (corner/sector/lap tips + post-session debrief). Strategy,
race-craft, and extra providers/sims are out of MVP. Convention: if a deferral needs data plumbed now
to avoid a future rewrite, that's listed under "Reserved in Phase 3".

Convert relative dates to absolute; this file is the source of truth for "почему этого нет в MVP".

---

## Deferred features

| Item | Deferred to | Reserved now (so it stays additive) | Why deferred | Ref |
|---|---|---|---|---|
| **Engine-map / ABS / TC advice** (e.g. "снизь TC до 2") | Later strategy/race-craft phase | The **data** is plumbed frame→Gold in Phase 3 (`EngineMap`, `Tc`, `TcCut`, `Abs` from ACC graphics page). Actions reserved as future registry entries. | Different domain from driving technique; would dilute MVP focus. Data is cheap to carry. | action-registry.md (Phase 2 race-craft); phase-3-detailed-plan.md |
| **Pit advisor** ("пора на пит") | Later strategy phase | `CoachCadence.Strategy` seam + a strategy quiet-zone declared in Phase 3; fuel/tyre/wear/pit-state fields plumbed frame→Gold. **Template-first, LLM-optional.** | MVP focus, not cost (cost is negligible). Needs its own cadence + timing + gate. | phase-3-detailed-plan.md |
| → pit-advisor **timing** (design note, not deferred) | — | Deliver on the **main straight / pit-window approach with lead time** (~1 lap before optimal), event-driven on fuel/tyre/mandatory-pit-window thresholds, gated so it never collides with a corner tip. **Not** on corner exit. | — | — |
| **Race-craft actions** (`defend_inside_at_corner`, `lift_coast_for_fuel`, `manage_brake_temp`, `gap_to_p2_holding`) | Phase 9+ | — | Requires opponents/strategy context beyond MVP. | action-registry.md |
| **Provisional best-of-session reference** (richer FR-014) | **Phase 6 (Reference-layer)** | Phase 3 ships the `NoPbYet` label + ≥2 reference-free corner actions so a first-ever session still coaches. | It's a `SimCoach.Reference` feature (resample the in-progress fastest clean lap onto the 1 m grid as a provisional reference), not Coach; needs more compute. The no-PB path covers MVP. | FR-014; phase-3-detailed-plan.md |
| **LLM token streaming** | **Phase 4** (voiced debrief — pulled from P6 per owner 2026-07-23) | The `StreamAsync` seam was declared in Phase 3 (threw `NotSupported`); Phase 4 implements it + the family-aware OpenRouter SSE decode for the route-agnostic streamed-prose voicer. | Real-time tips stay buffered (whole JSON needed before acting); streaming feeds the long-form debrief prose voiced in P4 via the new plain-text `debrief_prose` route (a 2nd billable call). | phase-4-detailed-plan.md (D1); ADR-0023 |
| **Prompt caching enablement** | Post-MVP tuning | Plumbing exists (`cached_input_tokens`, migration `002`); not enabled in Phase 3. | Cost is already under budget; enabling later only lowers it. | phase-3-detailed-plan.md |
| **Premium real-time models** (`claude-haiku-4.5` for corner/sector/lap) | Opt-in, post-MVP default | Model id is config per cadence; swap is config-only. (Debrief default **is** premium — Sonnet 4.6 — because it's one cheap call/session.) | Cheap Gemini/DeepSeek is good enough real-time; premium is a paid-tier toggle. | ADR-0004; phase-3-detailed-plan.md (M1) |
| **Gemini 3.x real-time** (3.5 Flash / 3 Flash) | Watch / not adopted | Default stays `gemini-2.5-flash-lite` (thinking fully off, TTFT ~0.26s, cheapest). **`gemini-3.1-flash-lite` is a named eval-gated UPGRADE** (~$0.014/session), not a hard deferral. | 3.x cannot fully disable thinking (`minimal` still reasons) → non-deterministic latency vs the hard 2000 ms buffered corner budget; task is reasoning-insensitive so the quality gain is marginal. 3.5 Flash overkill for an ≤8-word phrase. | phase-3-detailed-plan.md (m1) |
| **Yandex SpeechKit TTS** | Phase 4, behind a feature flag | — | Silero v5 RU ONNX is the in-proc default; Yandex is a fallback. | ADR-0005; implementation-plan.md (Phase 4) |
| **iRacing / LMU / F1 25 adapters** | Phases 8–10 | Sim-agnostic `TelemetryFrame` + provider seams already isolate this. | MVP is ACC-only. | implementation-plan.md |

## Carried from Phase 3 into later phases (PR-H closeout)

Phase 3 closed at PR-H with no follow-up PR in the phase, so anything not finished lands in a real **future
phase** (not as Phase-3 dirt). Consolidated here so the next phase's decomposition can't lose them:

- **Voice / TTS sink** → **Phase 4.** Coach emits `CoachTip`s to `ICoachTipSink`; the speaking sink is P4.
- **Avalonia overlay sink** → **Phase 5.** A second `ICoachTipSink` rendering tips on the transparent overlay.
- **Global mute hotkey (FR-044, `Ctrl+Alt+M`)** → **Phase 5** (moved from P4, 2026-07-23). A headless
  Generic Host has no HWND / message pump for `WM_HOTKEY`; the overlay window (P5) provides the HWND + pump.
  P4 ships mute **state** only (`IMuteState` + `voice.mute`/`voice.mute_on_startup` toggle; `hotkey.mute`
  key reserved). See phase-4-detailed-plan.md (D2) / ADR-0023.
- **Debrief *window* + remaining `004` columns** → **Phase 6.** The debrief headline tip, its structured loss
  attribution (`top_losses_json`, `setup_hint`), **and now the voiced debrief AUDIO + `StreamAsync`
  consumption land in Phase 4** (owner 2026-07-23 — the route-agnostic streamed-prose voicer, the plain-text
  `debrief_prose` route, and the `audio_artifact_ref` write). What stays P6: the post-session **window** that
  renders them and the remaining `004` columns (`debrief_prose` text, `checklist_json`,
  `per_sector_deltas_json`, `balance_verdict`).
- **`IReferenceQueryRepository` / `ISessionHistoryRepository` implementations** → **P6/P7.** Declared (with
  DTOs) in PR-H; the SQLite impls + the history/reference UI come with their screens.
- **Provisional best-of-session reference (richer FR-014)** → **Phase 6 (Reference-layer)** — see the table above.
- **ACC tyre-degradation source (FR-060)** → **Phase 6.** The thermal/wear summary plumbing exists; the
  degradation-rate source is P6.
- **Live (no-restart) monthly-budget re-bind** → **Phase 5 (settings UI).** The cap is honored from the stored
  `budget.monthly_usd` row at **startup** in P3; `ISettingsStore.SetMonthlyBudgetUsdAsync` is the P5 write side,
  and the live re-bind (binding `RuleEngineOptions` via `IOptionsMonitor`) lands with that UI. The `Llm:Live` /
  model / reasoning overrides already re-bind live via `IOptionsMonitor<LlmOptions>`.
- **`RecentContact` quiet-zone gate** → **future phase (needs a contract field).** The gate exists in the
  RuleEngine but `LiveCoachAmbientState` publishes `Contact: false` permanently: `TelemetryFrame` exposes only
  tyre-patch geometry, no collision/impact channel. Wiring it needs a new telemetry-contract field.
- **Strategy / pit advisor + engine-map/ABS/TC advice actions** → later race-craft phase — see the table above
  (the data is already plumbed frame→Gold; only the actions + cadence timing are deferred).
- **Live OpenRouter call** → **not a phase, a flag.** `Llm:Live=false` ships as default; flip it (settable via
  the settings store, no recompile) after the RU-eval + schema-acceptance pass. Until then every route resolves
  to the network-free fake provider, so the host is fully exercised offline.

## UI surfaces (roadmapped, not "cut" — listed so scope is explicit)

These are normal phase work, not MVP cuts, but Phase 3 reserves their **contracts** so they don't force a rewrite:
overlay (P5), voice/audio controls (P4/P5), settings panel (P5), post-session debrief window (P6),
cost/usage dashboard (P6/P7), session/reference history (P6/P7), first-run wizard (P7),
auto-update / crash-reporting (P7). The Phase-3 contracts they bind to (the `CoachTip` DTO,
`ICoachTipSink`, cost-query over `llm_usage`, settings store, tip/debrief persistence) are specified in
[ui-client-requirements.md](../03-functional/ui-client-requirements.md).

## Out of scope for this product (not deferred — excluded)

- DLL injection / memory writes (ADR-0007) — never.
- Sending raw telemetry off-machine — only Gold-tier JSON leaves (privacy).
