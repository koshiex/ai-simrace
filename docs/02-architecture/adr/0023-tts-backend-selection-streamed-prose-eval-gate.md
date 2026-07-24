# ADR-0023: TTS backend selection, streamed-prose voicing, the TTS-eval gate, and the debrief-audio boundary

**Status**: Accepted
**Date**: 2026-07-23

Supersedes-in-part: **ADR-0005** (Silero-primary) — on a failed V0 validation the default flips to Yandex
(see "V0 gate" below). Builds on ADR-0004 (provider-agnostic LLM seam), ADR-0011 (storage layout).

## Context

Phase 4 turns the Coach engine's `CoachTip` stream into spoken Russian. Planning surfaced four decisions
the owner had to settle before the work is decomposable (2026-07-23). They are recorded here because each
shapes a public seam and would be expensive to reverse mid-phase.

1. **Is Silero v5 actually usable in-proc?** ADR-0005 pins it but the risk register says "validate before
   Phase 4." An unusable ONNX export must not be discovered halfway through.
2. **Short spoken phrase vs streamed prose for the post-session debrief** — and, more generally, whether
   the voicing layer is debrief-specific or a reusable capability for *any* long-form output.
3. **How audio survives host shutdown** so the terminal debrief utterance is heard, not cut off.
4. **What the blocking quality bar is**, given Phase-3's lesson (the deterministic spine passed while the
   audible/semantic output was bad, because no eval gate covered it).

## Decision

- **V0 Silero spike gates the phase.** A throwaway `tools/SimCoach.SileroSpike` validates that a `v5_ru`
  ONNX export loads via `Microsoft.ML.OnnxRuntime` (CPU EP), streams chunked 16-bit PCM, and honours stress
  marks (`торм+оз`), within the FR-040 latency budget and the ADR-0005 size cap. **PASS → Silero is the
  default backend. FAIL → the default flips to Yandex SpeechKit** (`Voice:Runtime:Engine=Yandex`, config
  only) and the Silero synthesizer ships as a throwing stub until an export exists. No production Silero
  code merges before V0 passes.

- **TTS backend selection is live / monitor-aware.** `SelectingTtsBackend : ITtsBackend` reads
  `IOptionsMonitor<VoiceOptions>.CurrentValue.Engine` on every call (mirroring `LlmRouter`), so a settings
  write to `voice.engine` re-binds the active backend with no restart. Runtime knobs bind a dedicated
  `Voice:Runtime` section; the settings-table keys reconcile as `voice.engine→Voice:Runtime:Engine`,
  `voice.enabled→Voice:Runtime:Enabled`, `voice.volume→Voice:Runtime:Volume`, `voice.mute→Voice:Runtime:Mute`,
  `voice.mute_on_startup→Voice:Runtime:MuteOnStartup` (the backend-selection file slice renames
  `Voice:Backend→Voice:Engine` and `Voice:Silero:Voice→Voice:Silero:Speaker`). `hotkey.mute` is P5-reserved
  (the global hotkey binding is Phase 5). **RuPhonetics** stress-mark insertion runs on the **Silero path
  only** (Yandex stresses natively) and **preserves** pre-baked marks — it never re-transliterates authored
  corner names.

- **Voicing is a general capability, not debrief-specific.** Two paths ship in Phase 4:
  - **Short structured tips** — `VoiceTipSink : ICoachTipSink` speaks every `CoachTip` (corner/sector/lap)
    by synthesizing the already-rendered `phrase_ru` directly. No streaming (the phrase is short and already
    computed). Non-blocking `EmitTipAsync` (enqueue + return), per the `ICoachTipSink` contract.
  - **Streamed long-form prose** — a route-agnostic `IStreamedProseVoicer` consumes
    `ILlmClient.StreamAsync` token deltas → a `SentenceChunker` → `ITtsBackend` per sentence →
    `PriorityAudioQueue`. **The debrief is the first consumer, but the seam is not debrief-bound** — any
    future prose-producing route reuses it. This is the owner's "flexible, not only debrief" requirement.
  - A **plain-text prose route** (`debrief_prose`, `debrief_prose_fallback`) returns free-form RU prose as
    **plain content, not a forced-tool JSON schema**, so OpenRouter SSE `choices[0].delta.content` carries
    the tokens across model families. This is a **second billable call** for the debrief (~1.8¢/session on
    Sonnet 4.6, negligible under NFR-007) in addition to the structured debrief tip. The short
    `top_priority` tip is still emitted and persisted; the prose call is the narration.
  - **The SSE decoder is family-aware from the start**: it accumulates both `delta.content` and
    `delta.tool_calls[].function.arguments`, mirroring the buffered `OpenRouterProvider.ExtractContent`
    family branch, so a future *forced-tool* streamed route also works without a decoder rewrite.
  - **Stream metering is explicit.** An internal `LlmStreamResult { IAsyncEnumerable<LlmDelta> Deltas;
    LlmUsage? TerminalUsage }` carries usage past the lazy-enumeration boundary; `CostMeterProvider` and
    `CircuitBreakerProvider` `StreamAsync` become **re-yielding async iterators** (not pass-throughs) that
    record in a `finally`. Mid-stream cancel writes **exactly one** `llm_usage` row (`status=cancelled`,
    truthful partial); fallback-once writes **exactly one** row (no double-count).

- **Audio drains at container disposal.** `IAudioDevice : IAsyncDisposable`; the queued utterances flush on
  disposal, which runs **after** every `IHostedService.StopAsync` — including `CoachService`'s debrief drain.
  **`IHostApplicationLifetime.ApplicationStopping` is NOT used** for this (it fires *before* `StopAsync`,
  which would cut the debrief off). Composition sets `HostOptions.ShutdownTimeout` explicitly (the framework
  default is 30 s and is currently never configured) to cover the drain + debrief. The drain timeout and the
  debrief-playback ceiling are `IOptions` constants with reserved settings keys (`P4-reserve`) so a later UI
  can tune them.

- **The TTS-eval gate is blocking and covers the audible half, not only DSP/text.** Three legs beyond the
  hermetic DSP/logic checks:
  1. **FR-040 is validated on real hardware** — a Windows-only perf test runs `SileroOnnxSynthesizer` on the
     CPU EP and timestamps NAudio buffer-fill over N≈100 utterances, asserting p100 ≤ 200 ms, in a perf-smoke
     tier **outside** the deterministic macOS lane. The fake-clock latency check is **renamed to a
     queue-plumbing assertion** and never labelled FR-040 (a fake clock measures logical time, not synthesis
     cost).
  2. **Golden-audio stress regression** — reference audio (WAV or phoneme/duration-stress vectors) baked from
     the V0-validated model; a per-release check that stress marks are *actually pronounced* and no gross
     mispronunciation has regressed (the RuPhonetics text-fixtures alone cannot catch a TTS that ignores the
     mark).
  3. **A scripted, blocking manual-acceptance protocol** — a fixed ~15–20-utterance script (every
     `racingLexicon` term + car-length plurals 1/2/5 + the longest corner names + a real 3-corner preempt
     sequence) played on real Windows audio, with an explicit pass checklist and an owner sign-off. This is
     the human analogue of the Phase-3 RU-eval judge, and like it, it is **blocking**, not advisory.

## Why

- Gating on V0 removes the single biggest Phase-4 risk (an unusable Silero export) before sunk cost, and the
  config-only fallback to Yandex means the phase proceeds either way.
- A route-agnostic prose voicer + a family-aware SSE decoder cost little more than a debrief-only path but
  make every future long-form surface (richer debrief, session summaries, coaching recaps) additive — the
  owner's explicit intent to "have the capability for everything important right away."
- Disposal-time drain is the only lifecycle point strictly after `CoachService`'s post-cancellation debrief
  drain; `ApplicationStopping` is provably too early.
- The eval gate directly answers Phase-3's failure mode: the hermetic gate proves DSP/logic, the real-perf
  test proves the latency SLA the driver feels, golden-audio proves pronunciation, and the scripted manual
  protocol proves the audible quality no automated check can.

## Consequences

- New seams: `IStreamedProseVoicer`, `SentenceChunker`, `IAudioDevice : IAsyncDisposable`, `IMuteState`,
  `SelectingTtsBackend`, `LlmStreamResult`, the `debrief_prose` route pair.
- **The global mute hotkey (FR-044, Ctrl+Alt+M) moves to Phase 5.** A headless Generic Host has no HWND or
  message pump to receive `WM_HOTKEY`; the Phase-5 overlay window provides both. Phase 4 ships mute *state*
  (`IMuteState` + `voice.mute`/`voice.mute_on_startup` settings toggle); the global hotkey *binding* is P5.
- Yandex SpeechKit ships now behind `Voice:Runtime:Engine=Yandex` + a `Voice:Live` flag, fully offline-tested
  via a fake gRPC channel; only the short RU phrase text ever leaves the machine (NFR-004).
- `Grpc.Net.Client` is a new top-level dependency (owner-approved for the Yandex path).
- Coverage numbers for `Audio.*`/`Voice.*` exclude the native shims (WASAPI/ONNX/Win32), which are covered
  only by the Windows-only manual/smoke tier.
