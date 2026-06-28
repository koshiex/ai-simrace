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

Done: **PR-A** — provider-agnostic `ILlmClient` seam (Ring-0 contract + records, `LlmOptions`/route/
provider config, internal `LlmRouter`/`FakeProvider`, `CoachCadence`; dead-until-wired, FakeProvider default).

- [ ] `GoldArtifactBuilder` per cadence
- [ ] `ActionRegistry` with ~20 actions + RU templates
- [ ] `PromptBuilder` system + few-shot
  - Inject a `corner_id → human name` map (from the vendored CrewChief landmark file) so the LLM
    says "Eau Rouge", not "turn 5". Naming lives here, NOT in compute — compute emits only the
    stable `corner_id` token. Fallback/derived tracks have no names → positional phrasing. See
    ADR-0010.
- [ ] `OpenRouterClient` with HTTP/2 streaming + structured output
- [ ] `CostMeter` to SQLite
- [ ] `CircuitBreaker` per-provider
- [ ] `RuleEngine` quiet zones
- [ ] Fallback template path
- [ ] Tests: mock OpenRouter, golden fixtures

## Phase 4 — Voice (week 5)

- [ ] Bundle Silero v5 RU ONNX model in installer
- [ ] `SileroOnnxSynthesizer` streaming PCM chunks
- [ ] `YandexSpeechKitClient` (behind feature flag, gRPC bidi)
- [ ] `PriorityAudioQueue` preemption + fade-out
- [ ] `NAudioPlayer` WASAPI shared
- [ ] Hotkey to mute
- [ ] Tests: cancellation latency, fade-out continuity

## Phase 5 — Overlay (week 6)

- [ ] Avalonia transparent topmost window with click-through interop
- [ ] Delta bar / sector bars / current tip / lap counter
- [ ] Settings panel for layout + opacity + font size
- [ ] Race mode toggle
- [ ] Auto-hide when game loses focus
- [ ] Cap at 30 Hz refresh

## Phase 6 — Post-Session Debrief (week 7)

- [ ] Session-aggregate Gold artifact
- [ ] DeepSeek V3.2 debrief prompt
- [ ] Debrief window: sector chart, trace overlay, TTS playback
- [ ] PDF/MD export
- [ ] **Real ACC tyre-degradation source for the debrief tyre summary (FR-060).** ACC exposes no
      tyre-wear channel — Phase 3 plumbs `end_tyre_wear_pct` as an honest-zero (deferred from PR-B,
      see `phase-3-detailed-plan.md` risks #2). Phase 6 owns the real source because the debrief is
      first *delivered* to the user here, so the zero would otherwise become visible: derive degradation
      from clean-lap pace fall-off across the stint and/or populate `StintSummary.tyre_degradation_pct`
      (the proto field reserved for this, `[]` in MVP). Until then the debrief copy states the ACC
      limitation rather than rendering a fake `0%`.

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
