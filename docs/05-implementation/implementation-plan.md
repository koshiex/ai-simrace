# Implementation Plan — SimCoach

This file expands `peaceful-tumbling-firefly.md` phases into concrete tasks.

---

## Phase 0 — Foundation (week 1)

- [x] Repo skeleton + `.gitignore`
- [x] Architecture docs v1 (PRD, competitive-analysis, architecture, ADRs 0001-0007, telemetry-schema, action-registry, FRs, privacy)
- [x] `SimCoach.sln` + project skeletons (csproj per module)
- [x] CI workflow (`dotnet test`, `dotnet format --verify-no-changes`)
- [x] Protobuf schema generation hooked into `SimCoach.Contracts.csproj`
- [x] Generic Host wiring in `SimCoach.App/Program.cs`

## Phase 1 — ACC Telemetry + MCAP Capture (week 2)

- [x] Port ACC shared-memory struct layouts to C# (`AccPhysicsPage`, `AccGraphicsPage`, `AccStaticPage`)
- [x] `AccSharedMemoryReader` busy-poll loop at 333 Hz
- [x] `AccFrameMapper` → `TelemetryFrame`
- [x] `IngestService` channel + MCAP rotating writer
- [x] Replay tool that re-emits MCAP at real time
- [x] Tests: capture fixture, replay, byte-identical events (real-ACC capture fixture still pending — needs a Windows machine)

## Phase 2 — Reference Laps + Deterministic Compute (week 3)

Per-PR status lives in [phase-2-detailed-plan.md](./phase-2-detailed-plan.md) (5 PRs, A–E).
Done: **PR-A** (#8) + **PR-B** (#9) + **PR-D** + **PR-E**. Phase 2 complete.

- [x] SQLite schema (`sessions`, `laps`, `references`, `settings`, `llm_usage`) — PR-A (#8)
- [x] Session identity: producer-allocated `SessionContext` + `SessionManager` owns row + dir (ADR-0011) — PR-B (#9)
- [x] Lap/sector segmentation + clean-lap predicate (`LapSegmenter`/`SectorSegmenter`) — PR-B (#9)
- [x] Compute kernels: brake-on/off, peak-brake, trail-brake-%, throttle-on, min-speed, understeer/oversteer — PR-B (#9), pure/dead-until-wired
- [x] Track model: vendored landmark dataset + derive fallback (`LandmarkDataset`/`TrackModelBuilder`/`TrackModelStore`) — PR-D, dead-until-wired
- [x] Parquet writer for per-lap channels, resampled to 1m position (`McapSegmentEnumerator`/`PositionResampler`/`LapParquetWriter`) — PR-D, dead-until-wired
- [x] `ReferenceStore` PB lookup by `(trackId, carId, weatherBucket)` + `ReferenceParquetCodec` — PR-E
- [x] `ComputeService` wiring + racing-line deviation (needs reference) — PR-E
- [x] Domain event emission (`CornerEvent`, `SectorEvent`, `LapEvent`, `SessionEvent`) via `DomainEventFanOut` — PR-E
- [x] Unit tests on synthetic + replay fixtures — synthetic `SimCoach.TestKit` + replay e2e in place (extended per PR)

## Phase 3 — Coach Engine + LLM (week 4)

Per-PR status lives in [phase-3-detailed-plan.md](./phase-3-detailed-plan.md) (8 PRs, A–H).
UI contracts this phase must expose: [ui-client-requirements.md](../03-functional/ui-client-requirements.md).
Scope deferred out of the MVP: [mvp-deferrals.md](./mvp-deferrals.md).

**Phase 3 complete** (PR-A–H merged; PR-H = the host-flip, wired + persisted + e2e). Per-PR status in
[phase-3-detailed-plan.md](./phase-3-detailed-plan.md). Carried into later phases:
[mvp-deferrals.md](./mvp-deferrals.md) → "Carried from Phase 3 into later phases (PR-H closeout)".

- [x] `GoldArtifactBuilder` per cadence — PR-D
- [x] `ActionRegistry` with ~20 actions + RU templates — PR-C
- [x] `PromptBuilder` system + few-shot — PR-D
  - Inject a `corner_id → human name` map (from the vendored CrewChief landmark file) so the LLM
    says "Eau Rouge", not "turn 5". Naming lives here, NOT in compute — compute emits only the
    stable `corner_id` token. Fallback/derived tracks have no names → positional phrasing. See
    ADR-0010.
- [x] `OpenRouterClient` structured output — PR-F (buffered; HTTP/2 **streaming deferred to Phase 6**, `StreamAsync` declared)
- [x] `CostMeter` to SQLite — PR-F (wired + session-attributed in PR-H)
- [x] `CircuitBreaker` per-provider — PR-F
- [x] `RuleEngine` quiet zones — PR-G (per-session + rolling-monthly budget gates wired in PR-H)
- [x] Fallback template path — PR-G
- [x] Tests: mock OpenRouter, golden fixtures, host-composition smoke + Coach replay e2e — PR-F/H

## Phase 4 — Voice (week 5)

Carried from Phase 3 (PR-H): the **voice/TTS `ICoachTipSink`** — Coach already emits `CoachTip`s; P4 adds the
speaking sink. See [mvp-deferrals.md](./mvp-deferrals.md) → "Carried from Phase 3 into later phases".

- [ ] Bundle Silero v5 RU ONNX model in installer
- [ ] `SileroOnnxSynthesizer` streaming PCM chunks
- [ ] `YandexSpeechKitClient` (behind feature flag, gRPC bidi)
- [ ] `PriorityAudioQueue` preemption + fade-out
- [ ] `NAudioPlayer` WASAPI shared
- [ ] Hotkey to mute
- [ ] Tests: cancellation latency, fade-out continuity

## Phase 5 — Overlay (week 6)

Carried from Phase 3 (PR-H): the **overlay `ICoachTipSink`** (render tips on the transparent window) + the
settings panel writing through `ISettingsStore` (the store + SQLite config-source re-bind already exist). See
[mvp-deferrals.md](./mvp-deferrals.md) → "Carried from Phase 3 into later phases".

- [ ] Avalonia transparent topmost window with click-through interop
- [ ] Delta bar / sector bars / current tip / lap counter
- [ ] Settings panel for layout + opacity + font size
- [ ] Race mode toggle
- [ ] Auto-hide when game loses focus
- [ ] Cap at 30 Hz refresh
- [ ] **M44 — overlay instead of silence:** tips suppressed by the cadence-governor (M10 per-lap cap /
      global cooldown) or other non-abstain gates are routed to the overlay (visual, no TTS) instead of
      being dropped — the info is preserved without audio clutter. The routing decision (`Silent(cadence)
      → overlay`) is ratified now (Phase-3 P1); the rendering lands here with the overlay. Distinguish a
      *cadence-suppressed* tip (show) from a genuine *abstain* / below-materiality tip (drop). See the
      `Silent` point in `RuleEngine`/`CoachService`. Origin: owner idea from in-game testing 2026-07-04.

## Phase 6 — Post-Session Debrief (week 7)

Carried from Phase 3 (PR-H): **debrief *delivery* + `StreamAsync` consumption**; the **`IReferenceQueryRepository`
/ `ISessionHistoryRepository` implementations** (declared with DTOs in PR-H); the **provisional best-of-session
reference (richer FR-014)**, a `SimCoach.Reference` resample feature reassigned Post-MVP → Phase 6; and the
tyre-degradation source (FR-060, below). See [mvp-deferrals.md](./mvp-deferrals.md) → "Carried from Phase 3
into later phases".

- [ ] Session-aggregate Gold artifact
- [ ] DeepSeek V3.2 debrief prompt
- [ ] Debrief window: sector chart, trace overlay, TTS playback
- [ ] PDF/MD export
- [ ] **Real ACC tyre-degradation source for the debrief tyre summary (FR-060).** ACC exposes no
      tyre-wear channel — `physics.TyreWear` is "Not used" (always 0 live), so Phase 3 plumbs
      `end_tyre_wear_pct` as an honest-zero (deferred from PR-B, see `phase-3-detailed-plan.md` risks #2).
      Phase 6 owns the real source because the debrief is first *delivered* to the user here, so the zero
      would otherwise become visible. Two candidate approaches (decided when live ACC stint captures exist
      to validate against — neither is implementable/validatable before Phase 6):
  - **Approach B — pace-fall-off proxy (primary candidate).** A new compute kernel: tyre degradation =
        the trend of clean-lap times across a stint (lap-time fall-off), written to
        `StintSummary.tyre_degradation_pct` (the proto field reserved for this, `[]` in MVP). ~1 day, but
        **unvalidated until real-data calibration**, and it overlaps the race-craft `stints` work (Phase 10).
  - **Approach C — plumb indirect ACC channels.** Surface what ACC *does* give beyond `TyreWear`
        (tyre temps / pressures drift over a stint) as raw frame→Gold input feeding the estimator. Only
        worth doing once a consumer (the Approach-B estimator) exists — plumbing it earlier is dead code
        under `TreatWarningsAsErrors`, so it lands together with B, not before.
  - Until either ships, the debrief copy states the ACC limitation rather than rendering a fake `0%`.

## Phase 7 — Beta polish (week 8)

- [ ] Velopack installer + auto-update channel
- [ ] First-run wizard
- [ ] Crash reporting (Sentry, opt-in)
- [ ] Closed beta with 5–10 ACC drivers
- [ ] Metrics: lap-time delta week 1 → week 4, mute rate

## Phase 8 — iRacing adapter (week 9-10)

- [ ] Port IRSDK shared-memory shape to C#
- [ ] `Adapters.IRacing` reader using `LapDeltaTo*Lap` channels directly
- [ ] EAC compatibility test
- [ ] iRacing-specific corner registry

## Phase 9 — LMU adapter

- [ ] TheIronWolf rF2 plugin variant detection
- [ ] LMU native 1.3+ shared memory if present
- [ ] `Adapters.LMU` reader

## Phase 10 — F1 25 adapter + race-craft coaching

- [ ] UDP listener on `127.0.0.1:20777` with packet format v2025 gate
- [ ] ERS / DRS-aware coaching
- [ ] Race-craft actions: overtake/defend/fuel/tyre management

---

## Module Ownership (solo dev, but for clarity)

| Module | Phase first touched | Notes |
|---|---|---|
| Contracts | 0 | Schema must be stable before Phase 1 |
| Adapters.ACC | 1 | |
| Pipeline | 1, 2 | |
| Storage | 1, 2 | MCAP → Parquet → SQLite |
| Reference | 2 | |
| Coach | 3, 6 | Cadences phased in |
| LLM | 3 | Mocked first |
| Voice | 4 | Silero first, Yandex behind flag |
| Audio | 4 | |
| Overlay | 5 | |
| App | 0, 5, 7 | Settings UI grows over time |

---

## Risk Register

| Risk | Phase | Mitigation |
|---|---|---|
| Silero v5 RU ONNX export missing / not as listed | 4 | Validate before phase 4; fall back to PyTorch via Python sidecar; Yandex SpeechKit always works |
| `mcap` C# bindings unstable | 1 | Hand-roll a minimal MCAP writer over the spec (it's a simple chunked file format) |
| Avalonia transparency click-through quirks on Windows 11 | 5 | Win32 P/Invoke fallback path; small spike before phase 5 |
| OpenRouter structured-output spec drift | 3 | Pin model IDs; add response post-validation; fall back to template |
| ACC's anti-cheat changes mid-MVP | 1+ | We never inject; should be unaffected |
| Solo dev velocity | all | Cut scope: skip phase 6/7 if needed and beta with phases 0-5 |
