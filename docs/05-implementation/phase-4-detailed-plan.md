# Detailed Plan — Phase 4 (Voice/TTS)

## Goal

Phase 4 turns the Coach engine's `CoachTip` stream into **spoken Russian coaching**. Voicing is a **general capability, not debrief-only** (owner-locked D1): two paths ship. **(i)** Short structured tips — `VoiceTipSink : ICoachTipSink` speaks *every* `CoachTip` (corner/sector/lap) by synthesizing the already-rendered `phrase_ru` directly, **no streaming**, non-blocking `EmitTipAsync`. **(ii)** Streamed long-form prose — a **route-agnostic** `IStreamedProseVoicer` consumes `ILlmClient.StreamAsync` token deltas → `SentenceChunker` → `ITtsBackend` per sentence → `PriorityAudioQueue`; the post-session debrief is the **first consumer**, but the seam is **not** debrief-bound (any future prose route reuses it, no rewrite). The debrief *window* stays P6; the *audio* lands now. It ships four owner-locked decisions (D1–D4, see below and ADR-0023): **(1)** a **Silero validation spike (V0)** that GATES the phase — if `v5_ru` ONNX won't stream chunked PCM with stress marks in-proc, Yandex SpeechKit becomes primary; **(2)** a **`YandexSpeechKitClient`** as a second `ITtsBackend` behind a feature flag (default Silero), fully offline-testable via a fake gRPC channel; **(3)** the **M40 streaming path** — implementing `ILlmClient.StreamAsync` + a **family-aware** OpenRouter SSE decode (the three *terminal* producers throw `NotSupportedException` today; the two metering decorators pass through — all rewired per V13/PR-J) and voicing the debrief prose token-by-token over a **second billable plain-text `debrief_prose` route** (in addition to the still-emitted structured `top_priority` tip); **(4)** a **blocking per-release TTS-eval gate** with three real legs beyond the hermetic DSP/logic checks (a real-hardware Windows-only FR-040 perf test, a golden-audio stress regression, and a scripted blocking manual-acceptance protocol) plus the deterministic hermetic legs (queue-plumbing latency, fade/preempt continuity, cancel-latency, RMS band, RU-pronunciation fixtures). The load-bearing constraints throughout: the sink is **non-blocking** (never stalls the 333 Hz-fed coach pipeline); the whole stack **builds and tests fully offline on macOS/CI** with no audio hardware and no network (WASAPI/ONNX-native behind `[SupportedOSPlatform("windows")]` seams for runtime-safety, selected only at the App composition edge; the **global mute hotkey moves to P5** — a headless host has no HWND/message pump); and the audio **drains at container disposal (`IAudioDevice : IAsyncDisposable`), which runs after every `StopAsync` including `CoachService`'s debrief drain**, so the terminal debrief utterance is never enqueued into a dead queue.

---

## Design decisions (taken before this plan)

| Decision | Rationale |
|---|---|
| **V0 Silero spike is work-item ZERO and GATES the phase (owner #1)** | ADR-0005 pins Silero v5 `v5_ru` in-proc via `Microsoft.ML.OnnxRuntime`, but `implementation-plan.md:184` Risk Register says "validate before phase 4". Throwaway `tools/SimCoach.SileroSpike` console, **binary PASS/FAIL**. No production `SileroOnnxSynthesizer` merges until V0 passes; on FAIL the default flips to `Voice:Runtime:Engine=Yandex` (config-only) and V2 becomes a throwing stub. Runs on macOS (ONNX CPU EP loads cross-platform; the spike writes WAV, never touches WASAPI). |
| **Backend selection is `IOptionsMonitor`-aware, NOT captured once** | Resolving `ITtsBackend` in a singleton via `sp => o.Engine switch{…}` reads `IOptions<T>.Value` once and freezes the backend for the process — contradicting `LlmRouter` (`LlmRouter.cs:66-90` re-reads `IOptionsMonitor.CurrentValue` per resolve so a settings write re-binds without restart) and the P5 settings UI that flips `voice.engine` live (`ui-client-requirements.md:305`, P3-now). **Fix:** `SelectingTtsBackend : ITtsBackend` holds `IOptionsMonitor<VoiceOptions>` + both concrete backends (registered as singletons) and reads `.CurrentValue.Engine` inside each `StreamAsync`/`SampleRateHz`/`Channels`. Mirrors `LlmRouter` exactly. |
| **The `voice.*` settings keys need `MapKey` rows or live re-bind is dead** | `SqliteSettingsConfigurationProvider.MapKey` (verified) whitelists only `model.corner`/`model.sector`/`model.lap`/`model.debrief`/`reasoning.debrief`/`budget.monthly_usd`/`llm.live` and returns `null` for everything else ("runtime-only state … not configuration"). So a stored `voice.enabled=false` re-binds nothing. **Fix (PR-C):** add `voice.enabled→Voice:Runtime:Enabled`, `voice.engine→Voice:Runtime:Engine`, `voice.volume→Voice:Runtime:Volume`, `voice.mute→Voice:Runtime:Mute`, `voice.mute_on_startup→Voice:Runtime:MuteOnStartup`. Combined with the monitor-aware selector this makes a live engine/enabled/mute flip work exactly like `llm.live`. (The `hotkey.mute` *binding* row is **not** added in P4 — the global hotkey moves to P5, see D2 below; P4 ships mute *state* only.) |
| **`VoiceOptions` binds a NEW `Voice:Runtime` sub-section, disjoint from the existing backend-selection `Voice` shape** | `appsettings.json:108-119` already ships `Voice:{Backend:"silero", Silero:{ModelPath,Voice:"aidar"}, Yandex:{Enabled,FolderId,VoiceName:"filipp"}}`. Binding a runtime `VoiceOptions{Enabled,Engine,Volume,…}` from `"Voice"` collides (there is no `Voice:Enabled`; `Voice:Yandex:Enabled` is a nested bool). **Fix:** the file-shape (`Voice:Backend`/`Voice:Silero`/`Voice:Yandex`) is renamed to `Voice:Engine` (enum-bindable) + `Voice:Silero:Speaker` for the backend-selection slice; the user-facing **runtime** knobs bind `Voice:Runtime:*`. Settings-table keys map to `Voice:Runtime:*` paths (`hotkey.mute` is P5-reserved — the global hotkey binding is Phase 5). |
| **Both backends declare their own native PCM format; no backend-side resample** | Contract is 16-bit PCM + truthful `SampleRateHz`/`Channels`. Silero v5 emits **mono** at a rate **measured in V0** (ADR-0005 pins no rate — do not hardcode "48 kHz native"); Yandex `StreamSynthesis` emits `LINEAR16` at the requested rate. The single 48 kHz-stereo mix-up (architecture.md §3.7) is `IAudioDevice`'s job, keeping each backend a pure, hardware-free text→PCM function. |
| **Yandex is a 2nd `ITtsBackend` NOW behind `Engine=Yandex`, fully offline-tested (owner #2)** | Built against an injected `ISpeechKitChannel`: `FakeSpeechKitChannel` replays canned `LINEAR16` with no network/key; `GrpcSpeechKitChannel` is live only under a `Voice:Live` flag (mirrors `Llm:Live`/`EnvGate`). **Auth:** Yandex SpeechKit v3 gRPC uses an `authorization` metadata header carrying an IAM token or API key + `folderId` in metadata — not a bare key param. MVP prefers the **API key** (no ~12 h IAM refresh loop), read from `YANDEX_SPEECHKIT_API_KEY` env var (never settings, NFR-004). `Endpoint`/`FolderId` are config, never a hardcoded host. |
| **`Grpc.Net.Client` + a vendored SpeechKit `.proto` are genuinely new deps — OWNER-APPROVED this session** | Verified: `Directory.Packages.props` pins `Grpc.Tools 2.66.0` + `Google.Protobuf 3.28.3` but **NOT `Grpc.Net.Client`** (the client stack that brings the HTTP/2 handler). AGENTS "ask before a new top-level dep": **owner approved `Grpc.Net.Client` this session** (recorded in ADR-0023); pin it in PR-E. The `ISpeechKitChannel` seam keeps the dep behind a fake so CI never dials gRPC. |
| **Ordering uses `CoachPriority.CompareTo` directly — no bit-packing, no flattened-int** | `CoachPriority.cs` doc explicitly says the total order is achieved "without any flattened-int encoding or phase-weight multiplier"; a `<<24`/`&0xFF_FFFF` mapper introduces magic numbers (forbidden under `TreatWarningsAsErrors`) and is redundant (`CoachPriority` is already `IComparable`). **Fix:** order utterances by `(IsCornerCritical, CoachPriority)` via a small `UtterancePriority.Compare` delegating to `CoachPriority.CompareTo`. `IsCornerCritical := Cadence==CoachCadence.Corner` is the FR-043 stale-class discriminant. |
| **`IAudioDevice` mirrors the ACC live/replay split; platform split is for RUNTIME-SAFETY, guarded with `OperatingSystem.IsWindows()`** | NAudio 2.2.1 is `netstandard2.0` and carries **zero `[SupportedOSPlatform]` attributes**, so `WasapiOut`/`MediaFoundationResampler` do **NOT** trip CA1416 — they compile cross-OS and throw `PlatformNotSupportedException` at runtime on macOS. The seam therefore exists for **runtime-safety** (never construct a Windows type off-Windows → no `PlatformNotSupportedException`) plus a **composition test** that Windows audio/device types are never constructed off-Windows, not to satisfy CA1416. `WasapiOut`/`MediaFoundationResampler` sit in `[SupportedOSPlatform("windows")]` files constructed only inside an `AddWindowsAudio` helper (mirroring `AddAccSource`). The OS branch uses `OperatingSystem.IsWindows()` for consistency with `TelemetryComposition.cs:166` (NOT because `RuntimeInformation.IsOSPlatform` is unrecognized — it **is** recognized by CA1416 on the repo SDK). |
| **The portable resampler is `WdlResamplingSampleProvider` (managed, `NAudio.Core`); MediaFoundation is Windows-only** | `MediaFoundationResampler` (NAudio.Wasapi) throws `PlatformNotSupportedException` off-Windows at runtime, so `MediaFoundationPcmResampler` — **our `IPcmResampler` wrapper**, not a NAudio type — lives in a `[SupportedOSPlatform("windows")]` file wired only at the App edge; `WdlPcmResampler` (our wrapper around `WdlResamplingSampleProvider`) is the default/portable impl for the fake path and the whole non-Windows run. |
| **`VoiceTipSink.EmitTipAsync` uses non-blocking `TryWrite` + inline drop policy — never `WriteAsync`** | `ICoachTipSink.cs:3-7` mandates non-blocking; `CoachService` `await`s each sink inline (verified `CoachService.cs` real-time + debrief dispatch). A bounded `Channel.WriteAsync` **awaits when full**, stalling the sequential dispatch and the upstream fan-out. On a full queue apply the FR-042 preempt / FR-043 stale-drop policy inline. `EmitTipAsync` receives `CancellationToken.None` on the live path (CoachService processes every event on `CancellationToken.None`), so a closed-queue enqueue is detected via the **queue's own state** (try/catch → Debug log), not `ct`. |
| **Audio drains at CONTAINER DISPOSAL, NOT via `ApplicationStopping` (owner D3)** | Verified: `CoachService.ExecuteAsync` drains the domain-event channel to completion on `CancellationToken.None` **after** `stoppingToken` fires, and emits the terminal `SessionEvent` debrief via `_sink.EmitTipAsync` during that drain. The reversed stop-order is `IngestService → ComputeService → LiveCoachAmbientState → CoachService → McapRecorderService → SessionManager`. If audio stopped before `CoachService`, the debrief utterance would enqueue into a dead queue. **`ApplicationStopping` is the WRONG hook** — it fires *before* `StopAsync`, which would cut the debrief off. **Fix:** `IAudioDevice : IAsyncDisposable` flushes queued utterances **on disposal**, which runs **after** every `IHostedService.StopAsync` (including `CoachService`'s debrief drain). PR-M sets `HostOptions.ShutdownTimeout` **explicitly** (framework default is 30 s, currently never configured) to cover drain + debrief. The drain timeout and the debrief-playback ceiling are `IOptions` constants **with reserved settings keys (`P4-reserve`)** so a future UI can tune them; `ValidateOnStart` reads the **configured** `ShutdownTimeout`, not an assumed value. |
| **Per-sink fault isolation bounds FAULTS, not latency; ordering matters (owner D1, F8)** | `CoachService` is a `BackgroundService`; under default `BackgroundServiceExceptionBehavior.StopHost` an unhandled sink exception tears down the host (the reason `HandleSafelyAsync` exists). The dispatch loop is a **sequential `await`**, so a *blocking* `VoiceTipSink` would stall `ConsoleTipSink` — the try/catch bounds a thrown fault, **not** blocking latency. **Fix:** `CoachService` takes `IEnumerable<ICoachTipSink>`, snapshots to `IReadOnlyList`, empty-collection guard, orders **`ConsoleTipSink` (durable persist) FIRST, `VoiceTipSink` second**, wraps each `EmitTipAsync` (rethrow `OperationCanceledException`, log-and-continue everything else). Any *awaiting* sink must be **last**. A composed fan-out test with a deliberately-blocking `VoiceTipSink` asserts `ConsoleTipSink` still persists within a bound — proving the voice sink is truly synchronous/non-blocking. |
| **The debrief prose is a SECOND, PLAIN-TEXT `debrief_prose` route, not the structured `debrief` route (owner D1)** | Verified: `OutputSchema.Debrief` returns structured JSON (`top_losses`/`top_priority`/`setup_hint`) — token-streaming a half-formed JSON object is unspeakable. The structured `debrief` route ships **byte-for-byte unchanged** (buffered `CompleteAsync`, headline tip + persisted `top_losses_json`/`setup_hint`, the P6 window). The prose narration is a **plain-text** `debrief_prose` route pair (`debrief_prose` + `debrief_prose_fallback`, same Sonnet 4.6 / Reasoning=Low, `Stream=true`) returning **free-form RU prose as plain content, NOT a forced-tool JSON schema** — so OpenRouter SSE `choices[0].delta.content` carries the tokens across model families. This is a **second billable Sonnet call** for the debrief (~1.8¢/session, negligible under NFR-007) **in addition to** the short `top_priority` tip (still emitted + persisted). It streams sentence-by-sentence into TTS. |
| **The SSE decoder is FAMILY-AWARE from the start (owner D1)** | The decoder accumulates **both** `delta.content` **and** `delta.tool_calls[].function.arguments`, mirroring the buffered `OpenRouterProvider.ExtractContent` family branch (~`OpenRouterProvider.cs:247`, `tool_calls` at line 248). `debrief_prose` uses the plain-content path; a future *forced-tool* streamed route reuses the same decoder with **no rewrite**. |
| **Stream metering carrier is EXPLICIT: `LlmStreamResult` + re-yielding meter iterators (owner D1)** | Terminal `usage` crosses the lazy-enumeration boundary via an internal `LlmStreamResult { IAsyncEnumerable<LlmDelta> Deltas; LlmUsage? TerminalUsage }`. `CostMeterProvider.StreamAsync` and `CircuitBreakerProvider.StreamAsync` become **re-yielding async iterators (NOT pass-throughs)** that record in a `finally`. Mid-stream cancel → **exactly ONE** `llm_usage` row `status=cancelled`; fallback-once → **exactly ONE** row. Tests for both. |
| **Layering: `IStreamedProseVoicer` interface in Coach, `StreamedProseVoicer` impl in Voice; debrief is one caller** | Verified `SimCoach.Coach.csproj` references only Contracts/Reference/LLM/Storage — **not Voice or Audio** (only `SimCoach.App.csproj` references both). A prose voicer in Coach would pull ONNX Runtime + NAudio into the Coach closure and break macOS/CI. So the **route-agnostic** `IStreamedProseVoicer` interface + a no-op default live in Coach (mirroring `ICoachTipSink`); the `StreamedProseVoicer` impl lives in Voice, registered at the App edge. The debrief is the **first consumer** — the seam is **not** debrief-bound; any future prose route reuses it. `SentenceChunker` (LLM-only dep) may stay in Coach. |
| **RuPhonetics PRESERVES pre-baked stress marks; it is a normalizer/gap-filler, not the author** | `prompt-style-guide.md:54`: stress marks are "pre-baked into the action templates; the LLM does not need to add them." So `CoachTip.PhraseRu`/`CornerNameSpokenRu` arrive at the sink already stress-marked and humanized (commit 712d557). RuPhonetics inserts marks only for a bare foreign term and must PRESERVE existing `+` marks and never re-transliterate authored corner names (masked by word/token boundary, not `IndexOf`). Silero-only (Yandex stresses natively). |
| **The TTS-eval gate speaks car-lengths, never metres** | `CarLengthGloss.cs` renders distance as "N корпус/корпуса/корпусов" and its doc states this is "the one magnitude the voice path is allowed to speak (raw metres/km/h/ms are not)"; the MEMORY note `ru-coaching-voice-style.md` says "car-lengths not metres." So delta/number fixtures assert car-length plurals with correct RU count agreement; a **known-bad anchor** is exactly a phrase leaking raw "N метра"/"+Nм"/"мс"/"км/ч" (the class `RuEvalOptions` already records catching). |
| **ADR-0023 (CREATED) + ADR-0005 amendment** | ADR-0005 assumes the export exists. **ADR-0023 already exists** (`docs/02-architecture/adr/0023-tts-backend-selection-streamed-prose-eval-gate.md`, Accepted 2026-07-23): TTS backend selection, the Silero validation gate, the route-agnostic streamed-prose voicer, the second-billable-call `debrief_prose` route, the family-aware SSE decoder, disposal-time audio drain, the hotkey→P5 move, and the full TTS-eval gate. It records the V0 PASS/FAIL contract, monitor-aware selection, `voice.engine↔Voice:Runtime:Engine` reconciliation, the fallback branch, and the "audio in P4 / window in P6" boundary. On V0 FAIL, ADR-0005 gets a "Superseded in part" (Yandex-primary) note. |

**Build-order:** `V0 (spike GATE) → V1 (options + monitor-aware selection + MapKey rows + fan-out ctor) → {V2 Silero ∥ V3 Yandex} → V4 RuPhonetics → Audio queue/device (V5..V11) → VoiceTipSink (V12) → StreamAsync producers (V13) → StreamedProseVoicer / debrief (V14) → TTS-eval gate (V15) → host wiring + e2e (V16)`.

---

## Architecture

### Seam diagram

```
 voice.engine / voice.enabled / voice.volume / voice.mute / voice.mute_on_startup  (settings table)
    │  SetAsync → SqliteSettingsConfigurationProvider.MapKey → Voice:Runtime:* → IConfiguration reload
    ▼  IOptionsMonitor<VoiceOptions>.CurrentValue re-binds live (mirrors llm.live)
 CoachService (BackgroundService; processes every event on CancellationToken.None)
    │  IEnumerable<ICoachTipSink>  — fan-out, EACH sink wrapped in try/catch (per-sink FAULT isolation)
    ├─► ConsoleTipSink (P3)   → coach_tips row  (awaited; durable; ordered FIRST)
    └─► VoiceTipSink (P4)     → EmitTipAsync = TryWrite + return Task.CompletedTask (non-blocking; ordered LAST)
           │  SpokenTextMapper.Map(tip): PhraseRu + CornerNameSpokenRu (strip "(N)"); NO number rendering
           ▼
       PriorityAudioQueue  (singleton; depth 1 in-flight + 1 pending; (IsCornerCritical, CoachPriority) order via
           │                CompareTo; stale-drop ≥1s corner /≥2s general on TimeProvider; 10–20ms SEQUENTIAL fade-out;
           │                volume; mute via IMuteState) — implements IAudioRenderSource.Read(Span<float>)
           │  pulls PCM on the DEVICE render thread (decoupled from the 333Hz coach thread); Read is NO-ALLOC
           ▼                              ▲ writes                    ▲ reads
       SelectingTtsBackend : ITtsBackend  │                          IMuteState  (voice.mute / voice.mute_on_startup)
         ├ Engine=Silero → SileroOnnxSynthesizer → RuPhonetics (Silero-only stress) → InferenceSession (FFI, IDisposable)
         └ Engine=Yandex → YandexSpeechKitClient → ISpeechKitChannel {GrpcSpeechKitChannel(live) | FakeSpeechKitChannel(CI)}
           │  16-bit PCM @ SampleRateHz/Channels → IPcmResampler (WdlPcmResampler | MediaFoundationPcmResampler[win])
           │    chain: Pcm16→float → resample rate → MonoToStereo → gain/mute/fade → 48kHz stereo float
           ▼
       IAudioDevice : IAsyncDisposable  ── WasapiAudioDevice [SupportedOSPlatform("windows")] | NullAudioDevice/FakeAudioDevice
           (DRAINS queued utterances ON DISPOSAL → runs AFTER every StopAsync incl. CoachService's debrief drain;
            NOT ApplicationStopping, which fires BEFORE StopAsync)

 (Global mute HOTKEY Ctrl+Alt+M → PHASE 5: a headless host has no HWND/message pump for WM_HOTKEY.
  P4 ships mute STATE only via IMuteState + the voice.mute settings toggle.)

 Debrief (M40, owner D1) — the FIRST consumer of the route-agnostic streamed-prose seam:
   CoachService.ProcessDebriefAsync → EmitTipAsync(Session-cadence top_priority tip) [UNCHANGED] → AWAIT INSERT of debrief row
       └─► IStreamedProseVoicer.SpeakAsync(gold, sessionId, ct)  (interface in Coach; NoOp default)
              → StreamedProseVoicer (in Voice): a SECOND billable call —
                 ILlmClient.StreamAsync("debrief_prose", Stream=true, plain content) → family-aware SSE decoder
                 → SentenceChunker → RuPhonetics → ITtsBackend → PriorityAudioQueue @ least-urgent band
                   (CoachPriority(Exit,int.MaxValue) equivalent → sorts last; non-preempting; corner-critical preempts it)
                 → template-fallback: no LLM stream → synth PhraseRu directly, still writes WAV + UPDATE
              → DebriefAudioArtifactWriter → WAV under data root → UPDATE audio_artifact_ref by row id  (P4 writes / P6 reads)
```

### Per-tip pipeline (real-time)

1. `CoachService` builds a `CoachTip` (already RU-rendered, car-lengths, spoken corner name) and calls each `ICoachTipSink.EmitTipAsync(tip, CancellationToken.None)` inside a per-sink try/catch.
2. `VoiceTipSink` checks `IOptionsMonitor<VoiceOptions>.CurrentValue.Enabled`; if false → `Task.CompletedTask`.
3. `SpokenTextMapper.Map(tip)` = `PhraseRu` verbatim, prefixed by `CornerNameSpokenRu` (strip a defensive trailing `(N)`); **no number rendering**.
4. `VoicePriority` derives `(IsCornerCritical, CoachPriority)` + a stale-TTL from `CoachCadence` (corner→1 s, else→2 s). A `SpokenUtterance` captures text + `CoachPriority` + `IsCornerCritical` + `GeneratedAtUtc`.
5. `queue.EnqueueAsync(utterance)` → non-blocking `TryWrite` into the capacity-1-pending slot; stale-drop at enqueue; promote-if-idle / preempt-if-outranks / park-or-replace-pending / drop-as-superseded. Returns a **completed** `ValueTask`.
6. On the device render thread, `Read(Span<float>)` pulls resampled float PCM for the active utterance(s), applies the fade envelope on preempt, volume gain (FR-045), mute (FR-044), zero-fills between utterances.

### Module map (Artifact | Project/path | Form)

| Artifact | Project / path | Form |
|---|---|---|
| `VoiceEngine` enum (`Silero`,`Yandex`) | `src/SimCoach.Voice/VoiceEngine.cs` | enum |
| `VoiceOptions` (+ `SileroOptions`,`YandexOptions`, `EnsureValid`) | `src/SimCoach.Voice/VoiceOptions.cs` | records, `IOptions`, `ValidateOnStart` |
| `SelectingTtsBackend : ITtsBackend` (monitor-aware) | `src/SimCoach.Voice/SelectingTtsBackend.cs` | `internal sealed` |
| `SileroOnnxSynthesizer : ITtsBackend, IDisposable` | `src/SimCoach.Voice/SileroOnnxSynthesizer.cs` | class (FFI `InferenceSession`) |
| `SileroModel` (ONNX load/warm-up, tensor build) | `src/SimCoach.Voice/Silero/SileroModel.cs` | `internal sealed`, FFI |
| `RuPhonetics` + `RuPhoneticsLexicon` (embedded JSON) | `src/SimCoach.Voice/Phonetics/RuPhonetics.cs`, `Data/racingLexicon.json` | pure + embedded resource |
| `YandexSpeechKitClient : ITtsBackend` | `src/SimCoach.Voice/Yandex/YandexSpeechKitClient.cs` | class |
| `ISpeechKitChannel` + `GrpcSpeechKitChannel` + `FakeSpeechKitChannel` | `src/SimCoach.Voice/Yandex/` | interface + win/live + fake |
| `VoiceServiceCollectionExtensions.AddVoice` | `src/SimCoach.Voice/VoiceServiceCollectionExtensions.cs` | DI wiring |
| `IAudioDevice` (`Start`/`StopAsync`; `IAsyncDisposable`) | `src/SimCoach.Audio/IAudioDevice.cs` | interface |
| `IAudioRenderSource` (`int Read(Span<float>)`) + `AudioFormat` | `src/SimCoach.Audio/` | interface + `readonly record struct` |
| `WasapiAudioDevice` | `src/SimCoach.Audio/Wasapi/WasapiAudioDevice.cs` | `internal sealed`, `[SupportedOSPlatform("windows")]` |
| `NullAudioDevice` (pull-and-discard) | `src/SimCoach.Audio/NullAudioDevice.cs` | `internal sealed` |
| `AudioOptions` (+ `EnsureValid`) | `src/SimCoach.Audio/AudioOptions.cs` | `sealed record`, `IOptions` |
| `SpokenUtterance` + `UtterancePriority.Compare` (no bit-packing) | `src/SimCoach.Audio/` | `sealed record` + `static` |
| `PriorityAudioQueue : IAudioQueue, IAudioRenderSource` + `LinearFadeEnvelope` + `DropReason` | `src/SimCoach.Audio/` | one public type/file; mutation in `internal sealed` state holder |
| `IPcmResampler` + `WdlPcmResampler` (**our** wrapper over `WdlResamplingSampleProvider`, NAudio.Core) + `Pcm16ToFloat` + `MonoToStereo` step | `src/SimCoach.Audio/Resampling/` | interface + portable impl + pure decode |
| `MediaFoundationPcmResampler` (**our** `IPcmResampler` wrapper over `MediaFoundationResampler`, NAudio.Wasapi) | `src/SimCoach.Audio/Wasapi/MediaFoundationPcmResampler.cs` | `internal sealed`, `[SupportedOSPlatform("windows")]` |
| `IMuteState` + `MuteState` | `src/SimCoach.Audio/IMuteState.cs` | small interface + `sealed` impl (state only; hotkey binding → P5) |
| `VoiceTipSink : ICoachTipSink` + `SpokenTextMapper` + `VoicePriority` | `src/SimCoach.Audio/` | 2nd sink + pure statics |
| `CompositeTipSink` (fan-out over `IReadOnlyList<ICoachTipSink>`) — if not folding into `CoachService` | `src/SimCoach.Coach/CompositeTipSink.cs` | `sealed`, interface-only |
| `IStreamedProseVoicer` (route-agnostic) + no-op default + `SentenceChunker` | `src/SimCoach.Coach/Debrief/` | interface + no-op + chunker (LLM-only) |
| `StreamedProseVoicer : IStreamedProseVoicer` + `DebriefProseOptions` + `DebriefAudioArtifactWriter` | `src/SimCoach.Voice/Debrief/` | `sealed`, in Voice (debrief = first caller) |
| `OpenRouterSseDecoder` + `OpenRouterProvider.StreamAsync` + `LlmRouter.StreamAsync` + `FakeProvider.StreamAsync` + `CostMeter`/`CircuitBreaker` metering | `src/SimCoach.LLM/` | new + edits |
| `coach.system.debrief_prose.v1.ru.txt` + `PromptBuilder.BuildDebriefProse` | `src/SimCoach.Coach/Prompts/`, `PromptBuilder.cs` | embedded resource + method |
| `MapKey` rows (`voice.*`, no `hotkey.mute` — hotkey → P5) + typed `voice.*` accessors | `src/SimCoach.Storage/Configuration/SqliteSettingsConfigurationProvider.cs`, `ISettingsStore.cs`/`SqliteSettingsStore.cs` | edits |
| `AddVoiceStack` composition (`AddWindowsAudio` `[SupportedOSPlatform]`) | `src/SimCoach.App/VoiceComposition.cs` | DI wiring, App edge |
| V0 spike harness | `tools/SimCoach.SileroSpike/` | throwaway console (excluded from installer) |
| TTS-eval harness | `tests/SimCoach.TtsEval/` | new test project (mirrors `tests/SimCoach.RuEval/`) |
| ADR-0023 (CREATED) + ADR-0005 amendment | `docs/02-architecture/adr/0023-tts-backend-selection-streamed-prose-eval-gate.md` | ADR |

**Package deltas:** `Grpc.Net.Client` (NEW — **owner-approved this session**; verify against the confirmed absence in `Directory.Packages.props`), the vendored SpeechKit `.proto` under a `Grpc.Tools` codegen `ItemGroup`. Silero and Audio need **no** new package (`Microsoft.ML.OnnxRuntime 1.20.1`, `NAudio 2.2.1`, `Microsoft.Extensions.TimeProvider.Testing 9.0.0` all pinned). **`.gitignore` fix (F2, confirmed):** `racingLexicon.json` at `src/SimCoach.Voice/Data/` **IS** git-ignored by the generic `data/` rule (`git check-ignore -v` → `.gitignore:27:data/`) and there is **no** `!src/SimCoach.Voice/Data/` negation. Add `!src/SimCoach.Voice/Data/` + `!src/SimCoach.Voice/Data/**` to `.gitignore`, `git add -f` the JSON, add a test over the **real** embedded `Load()`, and a `ValidateOnStart` resource-existence check. Same trap as `SimCoach.Reference/Data/` (see `ai/knowledge-base/tools/dotnet-build-quirks.md`).

---

## Wiring into the live host

`AddVoiceStack(builder)` is invoked from `CoachComposition.AddCoachStack` (right after `AddLlm`/`AddCoaching`, verified `CoachComposition.cs`). Its stop-order-sensitive registrations:

- **`ITtsBackend`/`SelectingTtsBackend`/`SileroOnnxSynthesizer`/`YandexSpeechKitClient`** — singletons, no hosted-service slot. `SileroOnnxSynthesizer` is registered as its **concrete** type (not only via the factory) so DI disposes its `InferenceSession` at shutdown. `%VAR%` expansion of `Silero.ModelPath` happens at the App edge (mirroring `ResolveDataRoot`), never in the `net9.0` Voice project.
- **`PriorityAudioQueue` + `IAudioDevice` + `IMuteState`** — singletons; `IAudioDevice : IAsyncDisposable` **drains queued utterances on disposal**, **NOT** via `ApplicationStopping` (which fires *before* `StopAsync` and would cut the debrief). Disposal runs *after* every `IHostedService.StopAsync` — including `CoachService`'s `CancellationToken.None` debrief drain — bounded by `AudioOptions.ShutdownDrainTimeout ≤ HostOptions.ShutdownTimeout`. **PR-M sets `HostOptions.ShutdownTimeout` explicitly** (framework default is **30 s**, currently never configured) to cover drain + debrief. A **real host-shutdown** test (`StopApplication` → full `StopAsync` sweep → disposal, **not** a bare `ApplicationStopping` token) asserts a Session-class utterance enqueued during the drain still plays to completion through the fake device.
- **Global mute hotkey → PHASE 5.** A headless Generic Host has no HWND/message pump for `WM_HOTKEY`; the P5 overlay window provides both. P4 registers **no** hotkey hosted service — it ships mute *state* only via the singleton `IMuteState` (seeded from `voice.mute_on_startup`, toggled via `voice.mute`), which the always-alive queue reads.
- **`ICoachTipSink` registrations**: `ConsoleTipSink` (already in `AddCoaching`, verified `CoachServiceCollectionExtensions.cs:64`, ordered **first**) + `VoiceTipSink` (**last**, must be non-blocking). `CoachService`'s ctor changes to `IEnumerable<ICoachTipSink>` so it resolves both (PR-B, trunk-safe; updates `CoachServiceTests`, `HostCompositionTests`, **and `CoachReplayE2ETests.cs:93`** — a 2nd call site in a different assembly, F7). P5 adds `OverlaySink` identically.
- **`IStreamedProseVoicer`**: no-op default (in `AddCoaching`); the real `StreamedProseVoicer` (from Voice) only when `voice.enabled` and an audio device is registered. The debrief is its first caller.

**macOS/CI offline-fakes selection** (mirroring `AddTelemetrySource`'s live/replay + `PlatformNotSupportedException` guard):

```
bool useRealHardware = OperatingSystem.IsWindows() && !audioOptions.ForceFakeDevice;
if (useRealHardware)  AddWindowsAudio(builder);   // [SupportedOSPlatform("windows")]: WasapiAudioDevice + MediaFoundationPcmResampler
else { NullAudioDevice + WdlPcmResampler; }       // FakeSpeechKitChannel unless Voice:Live
```

A **DI-construction composition test** asserts Windows audio/device types are **never registered or constructed** off-Windows (runtime-safety against `PlatformNotSupportedException`; NAudio 2.2.1 has no `[SupportedOSPlatform]` attrs so there is no CA1416 to lean on — F1).

---

## Key C# contracts

```csharp
// src/SimCoach.Voice/SelectingTtsBackend.cs — monitor-aware; mirrors LlmRouter's per-call CurrentValue read
internal sealed class SelectingTtsBackend : ITtsBackend
{
    private readonly IOptionsMonitor<VoiceOptions> _options;
    private readonly SileroOnnxSynthesizer _silero;   // both concretes are singletons; only the selected one runs
    private readonly YandexSpeechKitClient _yandex;
    private ITtsBackend Selected => _options.CurrentValue.Engine switch
    {
        VoiceEngine.Silero => _silero,
        VoiceEngine.Yandex => _yandex,
        VoiceEngine v => throw new InvalidOperationException($"Unknown voice engine {v}."),
    };
    public IAsyncEnumerable<ReadOnlyMemory<byte>> StreamAsync(string text, CancellationToken ct)
        => Selected.StreamAsync(text, ct);            // re-selected per call → a live voice.engine flip takes effect next tip
    public int SampleRateHz => Selected.SampleRateHz;
    public int Channels     => Selected.Channels;
}
```

```csharp
// src/SimCoach.Audio/IAudioDevice.cs — the ONLY Windows-runtime boundary; pulls from a render source on its own thread
public interface IAudioDevice : IAsyncDisposable
{
    AudioFormat Format { get; }                 // fixed 48 kHz stereo (AudioOptions), the resample target
    void Start(IAudioRenderSource source);      // binds the queue's mixer; begins the pull loop
    Task StopAsync(CancellationToken ct);       // flush + stop; awaited AFTER the coach debrief drain
}
public interface IAudioRenderSource { AudioFormat Format { get; } int Read(Span<float> buffer); } // never throws; zero-fills when idle
public readonly record struct AudioFormat(int SampleRateHz, int Channels);

// Immutable enqueue capture — the queue never re-reads CoachTip. SpokenText built by the sink from PhraseRu + CornerNameSpokenRu.
public sealed record SpokenUtterance(string SpokenText, CoachPriority Priority, bool IsCornerCritical, DateTimeOffset GeneratedAtUtc);

internal static class UtterancePriority   // uses CoachPriority.CompareTo DIRECTLY — no flattened int, no <<24, no mask
{
    public static int Compare(SpokenUtterance a, SpokenUtterance b)
    {
        int c = b.IsCornerCritical.CompareTo(a.IsCornerCritical);   // true (corner-critical) sorts first
        return c != 0 ? c : a.Priority.CompareTo(b.Priority);       // CoachPriority: lower == more urgent
    }
    public static bool Outranks(SpokenUtterance cand, SpokenUtterance inc) => Compare(cand, inc) < 0;
}
public enum DropReason { Stale, Superseded, Muted }

public interface IAudioQueue
{
    ValueTask EnqueueAsync(SpokenUtterance u, CancellationToken ct);  // NON-BLOCKING: bounded slot swap + COMPLETED task
    void SetVolume(int volume0to100);   // FR-045
    void SetMuted(bool muted);          // FR-044 mute STATE (via IMuteState; global hotkey binding → P5)
}
```

```csharp
// src/SimCoach.Audio/PriorityAudioQueue.cs (sketch)
public sealed class PriorityAudioQueue : IAudioQueue, IAudioRenderSource
{
    private readonly ITtsBackend _tts; private readonly IPcmResampler _resampler;
    private readonly AudioOptions _options; private readonly TimeProvider _clock; private readonly ILogger _logger;
    // mutation isolated to an internal sealed state holder (in-flight, 1 pending slot, active LinearFadeEnvelope,
    // volume gain, mute flag) under a short lock NOT held across TTS/resample I/O.

    public int Read(Span<float> buffer)          // DEVICE render thread; deterministic w.r.t. the injected clock; NO-ALLOC
    {                                            // (pre-allocated scratch / ArrayPool; no LINQ, no boxing — allocation test asserts 0)
        // 1. mute wins over the in-flight fade (FR-044): clear + advance stream offset so unmute resumes correctly.
        // 2. if a fade is armed, ramp in-flight gain 1→0 over round(FadeOut*rate) FRAMES (sample-pairs, not interleaved samples);
        //    SEQUENTIAL fade-out-then-start: outgoing fades fully to 0, THEN the newcomer begins. No MixingSampleProvider, no crossfade.
        // 3. pull resampled float PCM (chain: Pcm16→float → resample rate → mono→stereo → gain/mute/fade); clamp |1.0|; zero-fill tail.
        //    managed DSP on the render thread → underrun risk; mitigated by 30–50ms WASAPI buffer + GCLatencyMode.SustainedLowLatency.
    }

    public ValueTask EnqueueAsync(SpokenUtterance u, CancellationToken ct)
    {
        if (IsStale(u)) { Log(DropReason.Stale, u); return ValueTask.CompletedTask; }        // FR-043 (enqueue + promotion)
        // depth 1 in-flight + 1 pending (FR-042): promote-if-idle / preempt-if-Outranks(inflight) /
        // park-or-replace-pending (drop lower as Superseded). NO inline TTS/device work. TryWrite-style; never awaits.
        return ValueTask.CompletedTask;
    }
    private bool IsStale(SpokenUtterance u)
    {
        TimeSpan age = _clock.GetUtcNow() - u.GeneratedAtUtc;
        return age >= (u.IsCornerCritical ? _options.StaleCornerCritical : _options.StaleGeneral);   // 1s / 2s
    }
}
```

```csharp
// src/SimCoach.Voice/VoiceOptions.cs — runtime knobs; binds "Voice:Runtime"; defaults from ui-client-requirements §3.8
public sealed record VoiceOptions
{
    public bool        Enabled       { get; init; } = true;                 // voice.enabled (P3-now, §3.8:304)
    public VoiceEngine Engine        { get; init; } = VoiceEngine.Silero;   // voice.engine; stored value "Silero" (ADR-0005:19,37; "Silero v5" is UI display only)
    public int         Volume        { get; init; } = 100;                  // voice.volume 0-100, DEFAULT 100 (§3.8:306)
    public bool        Mute          { get; init; }                         // voice.mute (state only; hotkey binding → P5)
    public bool        MuteOnStartup { get; init; }                         // voice.mute_on_startup (§3.8:307)
    public int         MaxPhraseChars{ get; init; } = 400;                  // > 0 guardrail
    public SileroOptions Silero { get; init; } = new(); public YandexOptions Yandex { get; init; } = new();
    public void EnsureValid()
    {
        if (!Enum.IsDefined(Engine)) throw new InvalidOperationException($"VoiceOptions.Engine '{Engine}' invalid.");
        if (Volume is < 0 or > 100)  throw new InvalidOperationException("VoiceOptions.Volume must be 0..100.");
        if (MaxPhraseChars <= 0)     throw new InvalidOperationException("VoiceOptions.MaxPhraseChars must be positive.");
        if (Engine == VoiceEngine.Silero && string.IsNullOrWhiteSpace(Silero.ModelPath))
            throw new InvalidOperationException("Silero.ModelPath required when Engine=Silero.");
        if (Engine == VoiceEngine.Yandex && string.IsNullOrWhiteSpace(Yandex.FolderId))
            throw new InvalidOperationException("Yandex.FolderId required when Engine=Yandex.");
        Silero.EnsureValid(); Yandex.EnsureValid();
    }
}
// YandexOptions: FolderId, VoiceName="filipp", ApiKeyEnvVar="YANDEX_SPEECHKIT_API_KEY" (env, NEVER settings, NFR-004),
//                Endpoint="tts.api.cloud.yandex.net:443" (config, no hardcoded host), SampleRateHz>0.
```

```csharp
// src/SimCoach.Audio/AudioOptions.cs (sketch)
public sealed record AudioOptions
{
    public int      DeviceSampleRateHz  { get; init; } = 48_000;    // arch 3.7
    public int      DeviceChannels      { get; init; } = 2;
    public TimeSpan DeviceBuffer        { get; init; } = TimeSpan.FromMilliseconds(40);  // WASAPI shared 30–50ms (F4: managed DSP underrun headroom)
    public TimeSpan FadeOut             { get; init; } = TimeSpan.FromMilliseconds(15);  // FR-042 (10–20ms), indexes FRAMES not interleaved samples
    public TimeSpan StaleCornerCritical { get; init; } = TimeSpan.FromSeconds(1);        // FR-043
    public TimeSpan StaleGeneral        { get; init; } = TimeSpan.FromSeconds(2);        // FR-043
    public int      DefaultVolume       { get; init; } = 100;       // bound; runtime value from voice.volume
    public bool     MuteOnStartup       { get; init; }              // bound; runtime value from voice.mute_on_startup
    public bool     ForceFakeDevice     { get; init; }              // force the fake on Windows for tests
    public TimeSpan ShutdownDrainTimeout{ get; init; } = TimeSpan.FromSeconds(5);        // P4-reserve key; ≤ configured HostOptions.ShutdownTimeout (30s default, set explicitly in PR-M)
    public void EnsureValid() { /* device sane; 10ms≤FadeOut≤20ms ∧ ≥1 frame ∧ ≤DeviceBuffer; general≥corner≥1tick;
                                  volume 0..100; ShutdownDrainTimeout>0 ∧ ≤ configured ShutdownTimeout */ }
}
```

```csharp
// src/SimCoach.Audio/VoiceTipSink.cs — the fire-and-forget speaking sink. Never throws; never awaits synth.
public sealed class VoiceTipSink : ICoachTipSink
{
    private readonly IAudioQueue _queue; private readonly IOptionsMonitor<VoiceOptions> _voice;
    private readonly TimeProvider _clock; private readonly ILogger<VoiceTipSink> _logger;
    public Task EmitTipAsync(CoachTip tip, CancellationToken ct)   // ct is CancellationToken.None on the live path
    {
        ArgumentNullException.ThrowIfNull(tip);
        if (!_voice.CurrentValue.Enabled) return Task.CompletedTask;
        string text = SpokenTextMapper.Map(tip);
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        var u = new SpokenUtterance(text, tip.Priority, tip.Cadence == CoachCadence.Corner, tip.GeneratedAtUtc);
        try { _ = _queue.EnqueueAsync(u, ct); }                    // completed ValueTask; TryWrite semantics
        catch (Exception ex) { _logger.LogDebug(ex, "Voice enqueue dropped (queue closing)"); } // closed-queue via queue state, not ct
        return Task.CompletedTask;
    }
}
```

```csharp
// src/SimCoach.Coach/Debrief/IStreamedProseVoicer.cs — ROUTE-AGNOSTIC interface in Coach (impl StreamedProseVoicer in Voice).
// The debrief is the FIRST consumer; the seam is NOT debrief-bound — any future prose route reuses it with no rewrite.
public interface IStreamedProseVoicer { Task SpeakAsync(GoldArtifact<GoldSessionPayload> gold, string sessionId, CancellationToken ct); }

// Global mute HOTKEY (Ctrl+Alt+M) → PHASE 5 (no HWND/message pump in a headless host). P4 ships mute STATE only:
public interface IMuteState { bool IsMuted { get; } void SetMuted(bool muted); }
```

```csharp
// src/SimCoach.Coach/Debrief/SentenceChunker.cs — deltas → whole RU sentences (protects RuPhonetics)
public sealed class SentenceChunker
{
    public async IAsyncEnumerable<string> ChunkAsync(IAsyncEnumerable<LlmDelta> deltas, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new StringBuilder(); int emitted = 0;        // mutation isolated to this local accumulator
        await foreach (LlmDelta d in deltas.WithCancellation(ct).ConfigureAwait(false))
        {
            buffer.Append(d.TextChunk);                            // LlmDelta = (TextChunk, FinishReason?)
            foreach (string s in DrainCompleteSentences(buffer))   // split on configured terminator+whitespace;
                if (!string.IsNullOrWhiteSpace(s) && emitted++ < _options.MaxSentences) yield return s.Trim();
                                                                   // NOT on decimal "0.6"; NOT after an abbreviation
            if (d.FinishReason is not null && buffer.Length > 0 && emitted < _options.MaxSentences)
            { yield return buffer.ToString().Trim(); buffer.Clear(); }   // residual flushed on the terminal delta
        }
    }
}
```

---

## Ordered work-items (V0..V16)

| ID | Module | Description | Deps | Tests | FR/refs |
|---|---|---|---|---|---|
| **V0** | `tools/SimCoach.SileroSpike` | **GATE.** Throwaway console loads a candidate `v5_ru.onnx` via `Microsoft.ML.OnnxRuntime` (CPU EP), synthesizes 3 canned RU phrases; measures first-frame latency, 20–40 ms PCM framing, stress-mark honoring (`торм+оз` A/B diff), model size, RTF; emits binary PASS/FAIL. FAIL → default `Engine=Yandex`, V2 = throwing stub. | — | manual verdict (runs on macOS, writes WAV) | FR-040, FR-041, NFR-005, ADR-0005 |
| **V1** | `SimCoach.Voice` + `Storage` + `Coach` | `VoiceEngine`, `VoiceOptions`/`SileroOptions`/`YandexOptions`+`EnsureValid`, `VoiceStartupValidator`, `SelectingTtsBackend`, `AddVoice`; appsettings `Voice:Backend→Voice:Engine` + `Voice:Silero:Voice→Speaker` rename + **`Voice:Runtime` block**; **`MapKey` rows** (`voice.enabled`/`voice.engine`/`voice.volume`/`voice.mute`/`voice.mute_on_startup` — **no `hotkey.mute`**, hotkey → P5); **`.gitignore` `!src/SimCoach.Voice/Data/` negation + `git add -f racingLexicon.json` (F2)**; typed `voice.*` accessors on `ISettingsStore`; `ITtsBackend.StreamAsync` doc-comment fix (`~200 ms p50 … NFR-001` → **FR-040 hard budget**, drop "p50" — F15). **`CoachService` ctor → `IEnumerable<ICoachTipSink>` moves to PR-B (V1-adjacent)**. | V0 | selection + live-flip (`IOptionsMonitor` change flips delegate) + undefined-engine ValidateOnStart throw + `MapKey`-drives-engine | ui-client §3.8, `LlmRouter`, `MapKey`, `ICoachTipSink` |
| **V2** | `SimCoach.Voice` | `SileroOnnxSynthesizer` + `SileroModel` (FFI, `IDisposable`, `[EnumeratorCancellation]` on `ct`), streaming 16-bit mono PCM in 20–40 ms frames. Ships real only if V0 PASS; else throwing stub. | V1 | Windows-trait model test + stub-`InferenceSession` frame test on `FakeTimeProvider` + mid-stream cancel | FR-040, FR-041, ADR-0005 |
| **V3** | `SimCoach.Voice/Yandex` | `YandexSpeechKitClient` + `ISpeechKitChannel` + `GrpcSpeechKitChannel` (`authorization` header + `folderId` metadata + HTTP/2 bidi) + `FakeSpeechKitChannel`; vendored `.proto` + codegen `ItemGroup`; **`Grpc.Net.Client` pin (owner sign-off)**; `Voice:Live` gate. | V1 | offline fake-channel re-emit + cancel + privacy (only `PhraseRu` crosses) | FR-046, NFR-004, ADR-0005 |
| **V4** | `SimCoach.Voice/Phonetics` | `RuPhonetics` + `RuPhoneticsLexicon` + `racingLexicon.json` (embed re-included via the `.gitignore` negation from V1/F2; test over the **real** embedded `Load()`) + word/token-boundary protected-span masking + numeric/positional reader; wire into Silero path. | V2 | real-embedded `Load()`; preserve pre-baked `+`; insert on bare foreign term; corner names byte-identical; substring-hazard; `поворот 7`→"седьмой"; known-bad stripped-`+` rejected | prompt-style-guide:54, ADR-0005 |
| **V5** | `SimCoach.Audio` | `AudioOptions`+`EnsureValid`+`AudioFormat`. | — | bounds / fade-band+≥1-sample+≤buffer / stale-ordering / volume range | FR-042, FR-043, FR-045, arch 3.7 |
| **V6** | `SimCoach.Audio` | `IAudioDevice`+`IAudioRenderSource`+`NullAudioDevice`; `FakeAudioDevice` (stereo, sample-capturing) in TestKit. | V5 | headless pull + sample capture | arch 3.7 |
| **V7** | `SimCoach.Audio` | `SpokenUtterance` + `UtterancePriority.Compare` (uses `CoachPriority.CompareTo`, **no bit-packing**). Adds `SimCoach.Coach` ProjectReference (or the sink builds a slim priority struct — open Q). | V5 | corner-critical dominance + phase/rank tie-break via real registry order + "delegates to CompareTo" | `CoachPriority.cs` |
| **V8** | `SimCoach.Audio` | `PriorityAudioQueue` enqueue / depth 1+1 / stale-drop (enqueue+promotion) / `DropReason` logs; `EnqueueAsync` completed `ValueTask`. **Core deliverable.** | V6,V7 | depth eviction (lowest dropped) / stale on fake clock / **non-blocking with a blocking fake TTS** | FR-042, FR-043 |
| **V9** | `SimCoach.Audio` | `LinearFadeEnvelope` + **sequential fade-out-then-start** preempt in `Read` (frame-indexed, channel-aware; no crossfade/mixer). | V8 | click-free splice sample-by-sample + clamp \|1.0\| | FR-042, arch 3.7 |
| **V10** | `SimCoach.Audio/Resampling` | `IPcmResampler` + `WdlPcmResampler` (portable) + `Pcm16ToFloat`; wire `ITtsBackend` PCM → float @ 48 kHz stereo on the pull thread. | V8 | 24 kHz sine → 48 kHz stereo length ratio + decode | arch 3.7 |
| **V11** | `SimCoach.Audio` | Volume (FR-045) + mute (FR-044 **state**) + `IMuteState` into `Read`. Read is **NO-ALLOC** (pre-alloc scratch/ArrayPool, no LINQ/boxing); `GCLatencyMode.SustainedLowLatency` around playback. | V9 | gain scaling / muted→silence while offset advances / mute overrides in-flight fade / **allocation test (`GC.GetAllocatedBytesForCurrentThread` delta==0 across N Reads)** | FR-044, FR-045 |
| **V12** | `SimCoach.Audio` | `VoiceTipSink` + `SpokenTextMapper` + `VoicePriority`; seed `IMuteState` from `MuteOnStartup`. **No hotkey service** — the global Ctrl+Alt+M binding moves to P5 (headless host has no HWND/message pump); P4 ships mute **state** only. | V1,V4,V11 | non-blocking/never-throws (SlowFakeQueue, ThrowingFakeQueue) / number-free mapping / `(N)` strip / null spoken name / mute-on-startup seed | FR-040, FR-044 (state), `ICoachTipSink` |
| **V13** | `SimCoach.LLM` | `LlmRouter.StreamAsync` (offline-swap + fallback-once on open) + `OpenRouterProvider.StreamAsync` real **family-aware** SSE decode (accumulates **both** `delta.content` **and** `delta.tool_calls[].function.arguments`, mirroring `ExtractContent` at ~`OpenRouterProvider.cs:247`, so a future forced-tool streamed route needs no rewrite; `stream:true`, `stream_options.include_usage`, `[DONE]`, drop empty-content thinking deltas) + `FakeProvider.StreamAsync` deterministic RU prose + **explicit** terminal-usage carrier `LlmStreamResult { IAsyncEnumerable<LlmDelta> Deltas; LlmUsage? TerminalUsage }` + `CostMeterProvider.StreamAsync`/`CircuitBreakerProvider.StreamAsync` become **re-yielding async iterators (NOT pass-throughs)** recording in a `finally` + **plain-text** `debrief_prose`/`debrief_prose_fallback` routes `Stream:true`. Trunk-safe, no Voice dep — land before V14. | — | mocked `HttpMessageHandler` SSE fixture (multi-chunk, keep-alive comment, thinking-only deltas, terminal usage, `[DONE]`, mid-stream 429→`LlmFailure`) / **mid-stream cancel → exactly ONE `llm_usage` row `status=cancelled`** / **fallback-once → exactly ONE row** / `FakeProvider` deterministic chunks | M40, FR-060/061 |
| **V14** | `SimCoach.Coach/Debrief` + `SimCoach.Voice/Debrief` | `IStreamedProseVoicer` (route-agnostic) + no-op default + `SentenceChunker` (Coach); `StreamedProseVoicer` (Voice) — a **SECOND billable call** on the plain-text `debrief_prose` route (the emitted `top_priority` tip is still persisted); **template-fallback**: no LLM stream → synth `PhraseRu` directly, still writes WAV + UPDATE. `DebriefProseOptions`+`EnsureValid` (incl. `ShutdownPlaybackCeiling ≤ HostOptions.ShutdownTimeout`); `DebriefAudioArtifactWriter` → deterministic WAV path under data root → `CoachTipRepository.UpdateAudioArtifactRefAsync` **by row id** (F9: no uniqueness on `(session_id, cadence='Session')` → the by-`session_id` UPDATE **races** the async INSERT on replay; fix = have `InsertAsync` return the id **OR** a NEW migration adding a UNIQUE index on `(session_id) WHERE cadence='Session'`); voicer **AWAITS** the debrief-row INSERT before UPDATE; one call in `ProcessDebriefAsync` **after** the headline emit; `coach.system.debrief_prose.v1.ru.txt` + `PromptBuilder.BuildDebriefProse`. | V8,V13 | chunker boundary (word/decimal/abbrev, residual on FinishReason, MaxSentences) / narrator at least-urgent band non-preempting / ceiling degrades to WAV-only / template-fallback path / UPDATE by id **after** INSERT (no race) | owner D1, M40, mvp-deferrals |
| **V15** | `tests/SimCoach.TtsEval` | New test project mirroring `tests/SimCoach.RuEval` (`EnvGate`, `FixtureLoader`, hermetic + env-gated split): `TtsEvalOptions`+`EnsureValid` (test-scoped, asserted by `[Fact]`), fixtures, `FadeAnalyzer`/`RmsAnalyzer`. **Full TTS-eval gate = hermetic DSP/logic legs (deterministic macOS lane) + 3 real legs (D4):** **(1)** FR-040 validated by a **real-hardware Windows-only perf test** (`SileroOnnxSynthesizer` on CPU EP + NAudio buffer-fill timestamp over N≈100 utterances, assert p100 ≤200ms) in a **perf-smoke tier OUTSIDE** the deterministic macOS lane — the fake-clock latency test is **RENAMED to a queue-plumbing assertion, never labelled FR-040**; **(2)** **golden-audio stress regression** (reference WAV / phoneme-duration-stress vectors baked from the V0-validated model; per-release assert stress marks are actually PRONOUNCED, no gross mispronunciation regression); **(3)** **A-Manual** — a NAMED, SCRIPTED, **BLOCKING** manual-acceptance protocol (~15–20 utterances: every `racingLexicon` term + car-length plurals 1/2/5 + longest corner names + a real 3-corner preempt sequence, on real Windows audio, explicit pass checklist + owner sign-off). | V9,V12 | queue-plumbing latency (fake clock) / fade-continuity / cancel ≤50ms / stale / RMS band / RU-pronunciation (car-lengths, stress marks preserved, foreign terms) / 20–40ms framing / **real-hw p100 ≤200ms perf-smoke** / **golden-audio stress** / **scripted manual protocol** | owner D4, FR-040..043, testing-strategy §5 |
| **V16** | `SimCoach.App` | `VoiceComposition.AddVoiceStack` (`AddWindowsAudio` `[SupportedOSPlatform]`, **explicit `HostOptions.ShutdownTimeout`** to cover drain+debrief, `IAudioDevice` disposal-time drain — **NOT** `ApplicationStopping`, settings-seeded volume/mute, `ValidateOnStart`); call after `AddCoachStack`; extend `HostCompositionTests`; offline replay e2e (FakeAudioDevice + FakeTtsBackend/FakeProvider). **F10:** the App defaults `RuntimeIdentifier=win-x64`, so the macOS offline e2e / `dotnet run` must **override the RID** (`-r osx-arm64` or unset) to load the osx-arm64 onnx native; `SileroOnnxSynthesizer` is constructed **only under `Voice:Live`/Windows** so the macOS lane never dlopens onnx. The single host-flip. | V12,V14,V15 | debrief-survives **real host shutdown** (`StopApplication`→full `StopAsync` sweep→disposal, not a bare `ApplicationStopping` token) / off-Windows device selection (DI-construction test) / replay e2e `NetworkCallCount==0` + local preemption invariant + **every `SpokenUtterance.SpokenText` reaching the queue passes a raw-unit-leak regex (no `3929мс`/`4 метра`/`км/ч`) — F13 voice-side backstop** | FR-042/043/044/045, NFR-004 |

---

## `ValidateOnStart` checklist (host crashes at startup, one test each)

1. `VoiceOptions.Engine` is a defined `VoiceEngine`; `Volume ∈ [0,100]`; `MaxPhraseChars > 0`.
2. `Engine=Silero` → `Silero.ModelPath` non-empty, `InferenceThreads>0`, `SampleRateHz>0`; the model file is probed only under `Voice:Live`/Windows (macOS/CI skip, mirroring `Llm:Live=false`).
3. `Engine=Yandex` → `Yandex.FolderId` non-empty, `SampleRateHz>0`; the `ApiKeyEnvVar`-named env var present **only** when `Voice:Live` (mirrors `EnvGate`); logs the NFR-004 "only the short RU phrase leaves" privacy notice.
4. `RuPhoneticsLexicon` embedded resource (`racingLexicon.json`, re-included past `.gitignore data/` via the F2 negation) resolves and parses (mirrors `PromptResources` existence check).
5. `AudioOptions.EnsureValid` — device rate/channels/buffer sane; `10 ms ≤ FadeOut ≤ 20 ms` ∧ `round(FadeOut*rate) ≥ 1 frame` ∧ `FadeOut ≤ DeviceBuffer`; `StaleGeneral ≥ StaleCornerCritical ≥ 1 tick`; `DefaultVolume ∈ [0,100]`; `ShutdownDrainTimeout ≤` the **configured** `HostOptions.ShutdownTimeout` (read from config, not an assumed value; PR-M sets it explicitly, framework default 30 s).
6. Off-Windows the resolved `IAudioDevice` is `NullAudioDevice` and `IPcmResampler` is `WdlPcmResampler`, never the Windows types (a **DI-construction** test asserts the Windows audio/device types are never registered or constructed off-Windows — runtime-safety, not CA1416).
7. `UtterancePriority.Compare` delegates to `CoachPriority.CompareTo` (no magic-int drift).
8. `IMuteState` seeds correctly from `voice.mute_on_startup` (pure, off-Windows; the global Ctrl+Alt+M **hotkey binding** validation is a P5 concern).
9. LLM stream: `debrief_prose` resolves + is rated; `debrief_prose.Stream==true` and `debrief.Stream==false`; `debrief_prose_fallback` resolves; fallback graph acyclic (existing check).
10. `DebriefProseOptions`: `MaxSentences>0`; terminators non-empty; `ShutdownPlaybackCeiling > 0` ∧ `≤ HostOptions.ShutdownTimeout`.
11. Writing `voice.enabled=false` via `SqliteSettingsStore` flips `IOptionsMonitor<VoiceOptions>.CurrentValue.Enabled` (regression guard the `MapKey` rows actually wired live re-bind).
12. The composed `IEnumerable<ICoachTipSink>` `CoachService` resolves contains both `ConsoleTipSink` and `VoiceTipSink` (and, if used, `CompositeTipSink` is not a member of its own list).

---

## Mergeable chunking (PR plan)

| PR | Scope | ~LOC | Classification |
|---|---|---|---|
| **PR-A** (`chore/tools`) | `tools/SimCoach.SileroSpike` + written PASS/FAIL verdict; ADR-0023 **already created** (`0023-tts-backend-selection-streamed-prose-eval-gate.md`) — add the ADR-0005 "superseded-in-part on V0 FAIL" amendment stub. | ~250 + ADR | throwaway |
| **PR-B** (`refactor(coach)`) | `CoachService` ctor → `IEnumerable<ICoachTipSink>` + fan-out with per-sink try/catch + empty guard; **`ConsoleTipSink` ordered first, any awaiting sink last**; update `CoachServiceTests`/`HostCompositionTests` **and `CoachReplayE2ETests.cs:93`** (2nd call site, different assembly — F7, or it won't build green) + composed fan-out test with a blocking sink (F8). | ~140 | existing-signature change, trunk-safe |
| **PR-C** (`feat(voice)`) | `VoiceEngine`, `VoiceOptions`/`SileroOptions`/`YandexOptions`+`EnsureValid`+`VoiceStartupValidator`, `SelectingTtsBackend`, `AddVoice`; appsettings `Voice:Engine`/`Voice:Silero:Speaker` rename + `Voice:Runtime` block + `MapKey` rows (**no `hotkey.mute`** — hotkey → P5) + `.gitignore` `!src/SimCoach.Voice/Data/` negation + `git add -f racingLexicon.json` (F2) + typed `voice.*` accessors; `ITtsBackend.StreamAsync` doc fix (FR-040, drop "p50" — F15). | ~450 | dead-until-wired + Storage edit |
| **PR-D** (`feat(voice)`) | `SileroOnnxSynthesizer` + `SileroModel` (FFI, `IDisposable`), streaming PCM *(real if V0 PASS, else throwing stub)*. | ~470 | dead-until-wired |
| **PR-E** (`feat(voice)`) | `YandexSpeechKitClient` + `ISpeechKitChannel` + `GrpcSpeechKitChannel` + `FakeSpeechKitChannel`; vendored `.proto` + codegen; `Grpc.Net.Client` pin (**owner-approved this session**); `Voice:Live` gate. | ~520 | dead-until-wired + owner-approved dep |
| **PR-F** (`feat(voice)`) | `RuPhonetics` + `RuPhoneticsLexicon` + `racingLexicon.json` + masking + numeric reader; wire into Silero. | ~320 | dead-until-wired |
| **PR-G** (`feat(audio)`) | **ONE cohesive `SimCoach.Audio` PR (F11 — merges the old PR-G/H/I so FR-042/043 tests pass together, not cross-deferred):** `AudioOptions`+`AudioFormat` + `IAudioDevice`/`IAudioRenderSource` + `NullAudioDevice`/`FakeAudioDevice` + `SpokenUtterance`/`UtterancePriority`; `PriorityAudioQueue` (depth/stale/`DropReason`) + `LinearFadeEnvelope` **sequential** fade-out (no crossfade/mixer); `IPcmResampler`/`WdlPcmResampler`/`Pcm16ToFloat`+`MonoToStereo` + NO-ALLOC `Read` + volume/mute/`IMuteState`. **Core.** | ~1240 | dead-until-wired |
| **PR-H** (`feat(audio)`) | `WasapiAudioDevice` + `MediaFoundationPcmResampler` (**our** `IPcmResampler` wrappers, `[SupportedOSPlatform("windows")]`, tests skipped off-Windows). | ~250 | Windows-only runtime |
| **PR-I** (`feat(voice)`) | `VoiceTipSink` + `SpokenTextMapper` + `VoicePriority`; `IMuteState` seeded from `MuteOnStartup`. **No hotkey service** — global Ctrl+Alt+M binding → P5. | ~330 | dead-until-wired |
| **PR-J** (`feat(llm)`) | `LlmRouter.StreamAsync` + `OpenRouterProvider.StreamAsync` **family-aware** SSE decode (`delta.content` + `delta.tool_calls[].function.arguments`) + `OpenRouterSseDecoder` + `FakeProvider.StreamAsync` + `CostMeter`/`CircuitBreaker` **re-yielding** metering iterators + `LlmStreamResult` terminal-usage carrier + plain-text `debrief_prose`/`debrief_prose_fallback` `Stream:true`. | ~450 | existing-stub → live, trunk-safe |
| **PR-K** (`feat(voice)`) | `IStreamedProseVoicer` (route-agnostic) + no-op default + `SentenceChunker` (Coach) + `StreamedProseVoicer`+`DebriefProseOptions`+`DebriefAudioArtifactWriter` (Voice, second billable call + template-fallback) + `CoachTipRepository.UpdateAudioArtifactRefAsync` **by id** + UNIQUE-index migration (or id-returning `InsertAsync`) + await-INSERT-before-UPDATE + `ProcessDebriefAsync` one call; mvp-deferrals/XML-doc edits. | ~480 | runtime-touching |
| **PR-L** (`test(tts-eval)`) | `tests/SimCoach.TtsEval` project + `TtsEvalOptions`/`EnvGate`/fixtures/analyzers + all hermetic gate legs (fake-clock latency **renamed to queue-plumbing**) + env-gated Yandex live leg + **real-hw perf-smoke tier** (FR-040 p100 ≤200ms, Windows-only) + **golden-audio stress leg** + **scripted blocking manual protocol**. | ~1300 | additive test gate |
| **PR-M** (`feat(app)`) | `VoiceComposition.AddVoiceStack` (`AddWindowsAudio`, **explicit `HostOptions.ShutdownTimeout`**, `IAudioDevice` disposal-time drain — **NOT** `ApplicationStopping`, settings-seeded volume/mute, `ValidateOnStart`) + call site + extended `HostCompositionTests` + offline replay e2e (RID-override for macOS, F10) + raw-unit-leak e2e backstop (F13) + `appsettings` runtime block. **The host-flip.** | ~340 | runtime-touching |

---

## Test strategy

All non-Windows tests run **fully offline** — fake queue/device/backend, `FakeSpeechKitChannel`, fake `IMuteState`, in-memory SQLite, `FakeTimeProvider` (pinned `Microsoft.Extensions.TimeProvider.Testing 9.0.0`) — no audio hardware, no network, no `Thread.Sleep`. Fakes are hand-rolled; `Moq` only for `HttpMessageHandler`/`ITtsBackend`.

- **RuPhonetics (table-driven):** foreign terms stressed; pre-baked `+` preserved; corner names byte-identical incl. substring hazard; `поворот 7`→"седьмой" (numeric reader exercised); known-bad stripped-`+` rejected.
- **Backend selection:** `Engine=Silero`→Silero, `=Yandex`→Yandex; a live `IOptionsMonitor` change flips the delegate on the next `StreamAsync`; undefined engine → `ValidateOnStart` throws.
- **Depth/preempt/fade (FR-042):** 3 enqueues → 1 in-flight + 1 pending + 1 dropped (lower-priority, `DropReason.Superseded`); preempt ramps in-flight gain 1→0 over `round(FadeOut*rate)` **frames** (sample-pairs), **sequential fade-out-then-start** (outgoing reaches 0 **before** the newcomer's first non-zero sample — no crossfade, no mixer), no sample exceeds \|1.0\|.
- **Stale-drop (FR-043):** advance the fake clock past 1 s/2 s → drop at enqueue **and** promotion; a fresh tip in the same slot is not dropped.
- **Queue-plumbing latency (NOT FR-040 — F6/D4):** a fake `ITtsBackend` whose first chunk is available at a controlled fake-clock offset → assert the queue exposes the first non-zero sample via `Read` within the budget. This is a **queue-plumbing assertion on logical time**, never labelled FR-040; the real FR-040 latency SLA is proven only by the Windows real-hardware perf-smoke tier (device buffer + GC + jitter).
- **Cancel-latency (testing-strategy §5):** cancel a mid-synth utterance on the fake clock → audio stops within ≤50 ms (`CancelLatencyMs`).
- **RMS band (testing-strategy §5):** captured PCM RMS in `[RmsFloor, RmsCeil]` — catches all-silence and clipping.
- **Volume/mute:** `SetVolume(50)` scales output independent of game; `SetMuted(true)` → all-zero while the stream offset advances; mute overrides in-flight fade.
- **Non-blocking sink:** a fake `ITtsBackend`/queue that would block if awaited → `EmitTipAsync` returns a synchronously-completed `Task`; a `ThrowingFakeQueue` still returns `Task.CompletedTask` and logs at Debug.
- **Fan-out fault isolation + non-blocking proof (F8):** one tip → both sinks; a *throwing* `VoiceTipSink` is isolated and `ConsoleTipSink`'s DB persist still happens; host does not fault. A **composed** fan-out test with a deliberately-**blocking** `VoiceTipSink` (ordered last) asserts `ConsoleTipSink` (ordered first) still persists **within a bound** — proving the voice sink is truly synchronous/non-blocking (the try/catch bounds faults, not latency).
- **StreamAsync (M40):** mocked `HttpMessageHandler` SSE fixture (multi-chunk RU, keep-alive comment, thinking-only empty-content deltas, `delta.content` **and** `delta.tool_calls[].function.arguments` family branches, terminal usage, `[DONE]`, mid-stream 429→correct `LlmFailure`); route-timeout via fake clock; **mid-stream cancel → exactly ONE `llm_usage` row (`status=cancelled`); fallback-once → exactly ONE row** (re-yielding meter iterators record in `finally`); `FakeProvider.StreamAsync` deterministic chunks.
- **SentenceChunker:** deltas split mid-`трейл-брейкинг` → whole sentences; decimal `0.6 с` not split; residual flushed on `FinishReason`; `MaxSentences` cap. Byte-identity: RuPhonetics through the chunked path == whole-text baseline.
- **Debrief-survives-shutdown (owner D3):** enqueue a Session-class utterance, trigger a **REAL host shutdown** (`StopApplication` → full `StopAsync` sweep → container disposal, **NOT** a bare `ApplicationStopping` token) → it plays to completion through the fake device on disposal-time drain; a narration past `ShutdownPlaybackCeiling` degrades to WAV-only without hanging teardown; template-fallback path writes WAV + UPDATE; `audio_artifact_ref` UPDATE (by row id) lands **after** the awaited debrief-row INSERT; headline still emitted. The "corner preempts debrief" leg is an **explicitly-SYNTHETIC queue unit-test** (F12: at shutdown `IngestService` is stopped, so it's unreachable as a real-world invariant).
- **Stop-order:** extend `HostCompositionTests` — the always-alive audio drains on disposal **after** every `StopAsync` (incl. `CoachService`'s debrief drain), `SessionManager` finalize is never delayed.
- **Offline replay e2e:** `Telemetry:Source=replay` + FakeAudioDevice + FakeProvider (RID overridden for macOS so no onnx dlopen, F10) → `StartAsync`→drain→`StopAsync`; assert utterances captured, `NetworkCallCount==0`, the **local preemption invariant** (`PreemptEvents` contains `incoming.Priority < interrupted.Priority` via `CoachPriority.CompareTo`) — **not** a global ascending-rank sort (a replay is time-ordered) — and **every `SpokenUtterance.SpokenText` reaching the queue passes a raw-unit-leak regex** (no `3929мс`/`4 метра`/`км/ч`, F13 voice-side backstop against the Phase-3 raw-number defect class).
- **TTS-eval gate (owner D4, blocking per-release):** the V15 harness, mirroring the RU-eval `EnvGate`/`RuEvalGateTests` shape. **The fake-clock latency check is a DSP/LOGIC gate (envelope monotonicity, drop policy, priority ordering), NOT a real-time-audio gate** (F6): it measures logical time, so it is **renamed to a queue-plumbing assertion, never labelled FR-040** — real first-audio/click/cancel latency (device buffer + GC + jitter) is validated **only** by the Windows manual/smoke tier. Hermetic legs always-on (phonetic preserve/insert with car-length + stress-mark fixtures and raw-unit known-bad anchors; fade; queue-plumbing latency; cancel; stale; RMS; 20–40 ms framing). Three real legs (D4): a **real-hardware Windows-only FR-040 perf-smoke** (`SileroOnnxSynthesizer` on CPU EP + NAudio buffer-fill timestamp, N≈100, p100 ≤200ms, outside the deterministic macOS lane); a **golden-audio stress regression** (stress marks actually pronounced); a **scripted, BLOCKING manual-acceptance protocol** (~15–20 utterances + owner sign-off). The real-Yandex contract is env-gated (`SIMCOACH_TTS_EVAL`).

**Coverage (grounded to `testing-strategy.md` line 3 / NFR-009):** `Audio.* ≥ 50%` (the real doc floor). **No `Voice.*` floor exists** in `testing-strategy.md` or NFR-009 (which names only Pipeline/Coach/Reference at 80% and Overlay at 50%) — propose `Voice.* ≥ 50%` by analogy, requiring a `testing-strategy.md` amendment + owner sign-off. All coverage numbers **exclude the native shims (WASAPI/ONNX/Win32), which are covered only by the Windows-only manual/smoke tier** — coverlet filters drop those FFI files from the denominator. `VoiceTipSink` is the **non-blocking load-bearing type** and is pinned to **80%** (relocate it into `Coach.*`-scoped coverage, or add an explicit coverlet include); it currently lives in `SimCoach.Audio`, so it counts under `Audio.*` unless relocated — confirm the assembly at PR time.

---

## Risks / open questions

| # | Risk / open question | Disposition |
|---|---|---|
| R1 | **V0 fails** — no usable `v5_ru` export, or it can't stream/honor stress marks (`implementation-plan.md:184`). | V0 gates the phase; ADR-0023 pre-writes the branch: default `Voice:Runtime:Engine=Yandex`, Silero seam = throwing stub, re-scope V2. Yandex "always works" per ADR-0005. |
| R2 | **PyTorch python-sidecar fallback** (Risk Register) violates in-proc (ADR-0005), offline, and single-binary ≤200 MB (NFR-005). | Evaluate but reject for MVP; last resort only if Silero fails **and** Yandex is unacceptable. Not built this phase. |
| R3 | **`Grpc.Net.Client` + vendored `.proto` are new top-level deps** (AGENTS: ask first). | Surface in PR-E; the `ISpeechKitChannel` fake keeps CI network-free. |
| R4 | **RuPhonetics contract** — `prompt-style-guide.md:54` says stress is pre-baked; ADR-0005 says RuPhonetics inserts it. Preserve-vs-author changes the fixtures. | Owner confirms the "normalizer/gap-filler" contract before PR-F/V15 freeze fixtures. Fixtures test preserve+gap-fill, not authorship. |
| R5 | **Silero native sample-rate / mono vs 48 kHz-stereo mix** could tempt in-backend resampling. | Backends declare their own rate + Channels=1, do NO resampling; the single mix-up is `IAudioDevice`/`IPcmResampler`. |
| R6 | **`SimCoach.Audio → SimCoach.Coach` reference** for `CoachPriority`/`CoachCadence`. | Recommend adding the ProjectReference (already `Audio→Voice`; Coach does not reference Audio, so no cycle). Alt: sink maps to a slim Audio-owned comparable struct. Owner picks in PR-G. |
| R7 | **WASAPI negotiated format** may reject exactly 48 kHz stereo on some hardware. | `WasapiAudioDevice` reads the device mix format at `Start` and sets the resampler's output rate to it; 48 kHz is the default/target. Windows integration smoke test (out of CI). |
| R8 | **`ShutdownPlaybackCeiling`/`ShutdownDrainTimeout` vs `HostOptions.ShutdownTimeout` (framework default 30 s, currently never configured)** — an over-ceiling flush is hard-killed. | PR-M **sets `ShutdownTimeout` explicitly** to cover drain+debrief; `ValidateOnStart` reads the **configured** value and asserts both `≤` it; the ceiling otherwise clamps and commits the WAV for P6 replay. Both are `IOptions` constants with reserved settings keys (`P4-reserve`). |
| R9 | **Debrief double-spend / P6 drift (D1).** | The `debrief_prose` narration **is** a second billable call (~1.8¢/session, negligible under NFR-007) in addition to the still-emitted+persisted `top_priority` tip; both grounded in the same Gold. `RefreshBudgetAsync` covers both; P6 and the pump read the same persisted structured row + WAV → no drift. |
| R10 | **Terminal usage crossing the `IAsyncEnumerable<LlmDelta>` boundary** — `LlmDelta` has no usage field. | Explicit carrier `LlmStreamResult { IAsyncEnumerable<LlmDelta> Deltas; LlmUsage? TerminalUsage }`; `CostMeter`/`CircuitBreaker` `StreamAsync` are **re-yielding iterators** recording in `finally`. Cancel → 1 `cancelled` row; fallback-once → 1 row. Tested both. |
| R11 | **Mid-stream primary failure fallback.** | Fall back only on stream **open**; a mid-stream failure surfaces as a truncated narration + logged warning (no re-stream). Confirm with owner. |
| R12 | **Global mute hotkey needs an HWND/message pump a headless host lacks (D2).** | **Moved to Phase 5** (overlay window provides HWND + pump). P4 ships mute **state** (`IMuteState` + `voice.mute`/`voice.mute_on_startup`); FR-044's hotkey **binding** lands P5. |
| R16 | **Managed DSP on the render thread → buffer underruns/clicks (F4).** | NO-ALLOC `Read` (pre-alloc scratch/ArrayPool, no LINQ/boxing, allocation-delta==0 test); WASAPI shared buffer bumped to 30–50 ms; `GCLatencyMode.SustainedLowLatency` around playback; a Windows real-device **soak** (underrun count) out of CI. |
| R13 | **Runtime mute persistence vs `MuteOnStartup`.** | `MuteOnStartup` seeds `IMuteState` at start; a runtime hotkey toggle is session-scoped unless the user pins it (then persist via `ISettingsStore`). Owner ergonomics call. |
| R14 | **Silero speaker id / installer bundling** — `aidar` default vs a bundled ~50 MB `v5_ru.onnx` (NFR-005). | V0 confirms `aidar` (owner eyeballs the WAV) and measures real size; bundle at install time (Velopack, ADR-0005), never commit to git/CI. Config-driven swap. |
| R15 | **Style-guide `"на 4 метра"` example (line 17) is stale** vs car-lengths (commit 712d557, `CarLengthGloss`). | Treat car-lengths as canonical (MEMORY + `CarLengthGloss` doc); flag the style-guide example for a docs fix. |

---

## Draft docs to amend

- **`docs/05-implementation/implementation-plan.md` §"Phase 4 — Voice (week 5)":** check off the delivered items — `SileroOnnxSynthesizer` streaming PCM, `YandexSpeechKitClient` (flag), `PriorityAudioQueue` preemption+fade, `NAudioPlayer`/`WasapiAudioDevice`, **mute state** (`IMuteState` + `voice.mute` toggle; the global Ctrl+Alt+M **hotkey binding** moves to Phase 5), **M40 streaming debrief via `IStreamedProseVoicer`**, "Tests: cancellation latency, fade-out continuity". Add the V0 spike gate, the TTS-eval gate, and the Silero-bundle-in-installer item. Note `NAudioPlayer`→`IAudioDevice`/`WasapiAudioDevice` rename. **Already APPLIED (2026-07-24):** the §"Phase 5 — Overlay" global-mute-hotkey bullet and the §"Phase 6" carried-sentence edit (owner D1: `StreamAsync` consumption + debrief audio → P4, window stays P6). The Phase-4 checkboxes above remain to be ticked as items ship.
- **`docs/05-implementation/mvp-deferrals.md` — APPLIED (2026-07-24):** the debrief row is split (the **window** + `debrief_prose`/`checklist_json`/`per_sector_deltas_json`/`balance_verdict` columns stay P6; the **audio** `audio_artifact_ref`+WAV and **`StreamAsync` consumption** → P4, now lines 41-46); the LLM-token-streaming row (line 23) now reads **Phase 4**; the "Voice / TTS sink → Phase 4" row references the route-agnostic `IStreamedProseVoicer`; and the global Ctrl+Alt+M mute **hotkey** is in the "Carried" list as a P4→P5 carry (line 37).
- **Code XML-doc / message edits** (so in-comment claims stop contradicting P4): `ILlmClient.StreamAsync` doc ("declared for P6, throw in Phase 3"), `LlmDelta` doc ("declared for P6; never produced in Phase 3"), and the three `NotSupportedException` messages in `LlmRouter`/`OpenRouterProvider`/`FakeProvider`; the `ITtsBackend.StreamAsync` doc ("~200 ms p50 … NFR-001" → FR-040 hard budget, drop "p50").
- **`docs/03-functional/functional-requirements.md`:** no FR renumber needed; optionally footnote FR-046 (Yandex auth is `authorization`-header API key/IAM, not a bare key) and FR-062 (audio in P4, window in P6).
- **`docs/06-style/prompt-style-guide.md` (line 17):** fix the stale `"на 4 метра"` example to car-lengths (`CarLengthGloss`); confirm line 54's "stress pre-baked" so the RuPhonetics contract is unambiguous.
- **`docs/04-testing/testing-strategy.md`:** if a `Voice.*` coverage floor is adopted, add it explicitly (line-1 table currently has no `Voice.*` entry); document the TTS-eval gate section (cancel-latency ≤50 ms, RMS band) alongside the RU-eval gate.
- **ADR-0023 — CREATED** (`docs/02-architecture/adr/0023-tts-backend-selection-streamed-prose-eval-gate.md`, Accepted 2026-07-23): records the V0 PASS/FAIL contract + fallback branch, monitor-aware backend selection + `voice.engine↔Voice:Runtime:Engine` key reconciliation, the route-agnostic streamed-prose voicer + second-billable `debrief_prose` route + family-aware SSE decoder, disposal-time audio drain + explicit `ShutdownTimeout`, the hotkey→P5 move, Silero-only RuPhonetics, the full blocking TTS-eval gate (real-hw perf + golden-audio + scripted manual), and the "debrief audio in P4 / window in P6" `audio_artifact_ref` boundary. **ADR-0005** is marked **"superseded-in-part on V0 FAIL (Yandex primary)"**.
