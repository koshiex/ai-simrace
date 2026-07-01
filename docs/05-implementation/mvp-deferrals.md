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
| **Provisional best-of-session reference** (richer FR-014) | Post-MVP | Phase 3 ships the `NoPbYet` label + ≥2 reference-free corner actions so a first-ever session still coaches. | A provisional reference needs more compute; the no-PB path is covered minimally for MVP. | FR-014; phase-3-detailed-plan.md |
| **LLM token streaming** | Phase 6 (debrief delivery) | The `StreamAsync` seam is declared in the provider-agnostic contract in Phase 3 (throws `NotSupported` until P6). | Real-time tips are buffered (whole JSON needed before acting); streaming only helps long-form debrief. | phase-3-detailed-plan.md (reasoning/streaming decision) |
| **Prompt caching enablement** | Post-MVP tuning | Plumbing exists (`cached_input_tokens`, migration `002`); not enabled in Phase 3. | Cost is already under budget; enabling later only lowers it. | phase-3-detailed-plan.md |
| **Premium real-time models** (`claude-haiku-4.5` for corner/sector/lap) | Opt-in, post-MVP default | Model id is config per cadence; swap is config-only. (Debrief default **is** premium — Sonnet 4.6 — because it's one cheap call/session.) | Cheap Gemini/DeepSeek is good enough real-time; premium is a paid-tier toggle. | ADR-0004; phase-3-detailed-plan.md (M1) |
| **Gemini 3.x real-time** (3.5 Flash / 3 Flash) | Watch / not adopted | Default stays `gemini-2.5-flash-lite` (thinking fully off, TTFT ~0.26s, cheapest). **`gemini-3.1-flash-lite` is a named eval-gated UPGRADE** (~$0.014/session), not a hard deferral. | 3.x cannot fully disable thinking (`minimal` still reasons) → non-deterministic latency vs the hard 2000 ms buffered corner budget; task is reasoning-insensitive so the quality gain is marginal. 3.5 Flash overkill for an ≤8-word phrase. | phase-3-detailed-plan.md (m1) |
| **Apex-window / on-a-straight / user quiet-zone gates** (if `normalizedCarPosition` not added) | Conditional | Either add `normalizedCarPosition` (+ corner-phase) to the gate snapshot in Phase 3, or these three gates ship deferred (and must not silently no-op). | Needs track-position in the gate snapshot; decided in the rework. | phase-3-detailed-plan.md (M7) |
| **Yandex SpeechKit TTS** | Phase 4, behind a feature flag | — | Silero v5 RU ONNX is the in-proc default; Yandex is a fallback. | ADR-0005; implementation-plan.md (Phase 4) |
| **iRacing / LMU / F1 25 adapters** | Phases 8–10 | Sim-agnostic `TelemetryFrame` + provider seams already isolate this. | MVP is ACC-only. | implementation-plan.md |

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
