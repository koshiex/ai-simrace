# Architecture — SimCoach

**Status**: Draft v1
**Audience**: contributors, code reviewers, security reviewers

---

## 1. Context (C4 Level 1)

```
┌──────────────────────────────────────────────────────────────────┐
│                       User's Windows PC                          │
│                                                                  │
│  ┌────────────┐   shared memory    ┌────────────────────────┐   │
│  │    ACC     │ ────────────────►  │     SimCoach.App       │   │
│  │  (game)    │                    │ (Windows desktop app)  │   │
│  └────────────┘                    └────────┬───────────────┘   │
│        ▲                                    │                   │
│        │  user                              │  HTTPS            │
│        │  inputs                            ▼                   │
│  ┌─────┴──────┐                    ┌────────────────────────┐   │
│  │  Driver    │                    │  Optional cloud:       │   │
│  │  (Russian  │  ◄── audio TTS ── │  • OpenRouter (LLM)    │   │
│  │  sim racer)│      overlay UI    │  • Yandex SpeechKit    │   │
│  └────────────┘                    └────────────────────────┘   │
└──────────────────────────────────────────────────────────────────┘
```

Only "Gold-tier" coaching artifacts (200–500 token JSON) leave the machine, addressed at the user-chosen LLM provider. Raw telemetry never leaves the local disk.

## 2. Container View (C4 Level 2)

```
┌────────────────────────── SimCoach.App ──────────────────────────┐
│                                                                  │
│  ┌──────────────┐   in-process     ┌──────────────────────┐      │
│  │  Adapters.*  │ ───── pub/sub ──►│  Pipeline.Ingest     │      │
│  │  (ACC MVP)   │                  │  + Pipeline.Compute  │      │
│  └──────────────┘                  └─────────┬────────────┘      │
│        ▲                                     │                   │
│        │ SHM read                            │ events            │
│        │                                     ▼                   │
│  ┌──────────────┐                  ┌──────────────────────┐      │
│  │   ACC.exe    │                  │  Reference.Store     │      │
│  └──────────────┘                  │  (SQLite + Parquet)  │      │
│                                    └──────────┬───────────┘      │
│                                               │                  │
│  ┌──────────────┐                             ▼                  │
│  │  Storage     │ ◄── MCAP write ──── ┌──────────────────────┐   │
│  │  (MCAP+SQL)  │                     │  Coach.Engine        │   │
│  └──────────────┘                     │  (Gold artifacts)    │   │
│                                       └────┬─────────────┬───┘   │
│                                            │             │       │
│                                            ▼             ▼       │
│                                  ┌──────────────┐ ┌──────────┐   │
│                                  │  LLM.Client  │ │  Voice   │   │
│                                  │  (OpenRouter)│ │  (Silero │   │
│                                  └──────┬───────┘ │   ONNX)  │   │
│                                         │         └────┬─────┘   │
│                                         ▼              ▼          │
│                                  ┌─────────────────────────┐     │
│                                  │  Coach.TipQueue         │     │
│                                  │  (priority + preempt)   │     │
│                                  └─────────┬───────┬───────┘     │
│                                            │       │             │
│                                            ▼       ▼             │
│                                  ┌──────────┐  ┌────────┐        │
│                                  │ Overlay  │  │ Audio  │        │
│                                  │ (Avalonia│  │ NAudio │        │
│                                  │ transp.) │  │ WASAPI │        │
│                                  └──────────┘  └────────┘        │
└──────────────────────────────────────────────────────────────────┘
```

## 3. Component View (selected modules)

### 3.1 Adapters.ACC
- `AccSharedMemoryReader` — opens `Local\acpmf_physics`, `Local\acpmf_graphics`, `Local\acpmf_static` via `MemoryMappedFile`; busy-poll at 333 Hz on a dedicated background thread.
- `AccBroadcastClient` — UDP listener on the ACC broadcasting port for opponents & race control.
- `AccFrameMapper` — converts native ACC structs into `SimCoach.Contracts.TelemetryFrame`.
- Reconnects automatically on game restart.

### 3.2 Pipeline
- `IngestService` — single `System.Threading.Channels.Channel<TelemetryFrame>` between adapter and consumers; bounded capacity 256; drop oldest under pressure.
- `MCAPRecorder` — rotating writer, 60 s segments, zstd compressed, indexed by message channel.
- `ComputeService` — per-frame derivations: brake-on, brake-off, peak-brake-pressure, throttle-on, min-speed, slip angle (when wheel-load + tyre-slip available), racing-line deviation, sector cross detection, lap completion.
- Emits domain events on additional Channels: `CornerEvent`, `SectorEvent`, `LapEvent`, `SessionEvent`.

### 3.3 Reference
- `ReferenceStore` — SQLite `references` table keyed by `(trackId, carId, weatherBucket)`; `path` to Parquet blob with channels resampled to 1m of `normalizedCarPosition`.
- `ReferenceLookup` — returns reference for current session if available; null otherwise (until enough laps to establish a PB).

### 3.4 Coach
- `GoldArtifactBuilder` — given a `CornerEvent` + reference channels, build a 200–500 token JSON with deltas.
- `ActionRegistry` — bounded set of allowed coaching actions (see `action-registry.md`).
- `PromptBuilder` — composes system + few-shot prompts; reads from `prompts/` resources.
- `TipQueue` — priority + preemption + fade-out cancellation; max depth 1 in-flight + 1 queued.
- `RuleEngine` — decides when *not* to speak (active braking zone, apex window, recently muted by user).

### 3.5 LLM
- `OpenRouterClient` — `HttpClient` with HTTP/2; supports both unary and streaming SSE; `response_format: json_schema, strict: true`.
- `CostMeter` — accumulates per-call input/output tokens into SQLite `llm_usage`.
- `CircuitBreaker` — opens after 3 consecutive failures in 60 s; closes after 60 s cool-down.

### 3.6 Voice
- `SileroOnnxSynthesizer` — in-process Silero v5 RU via `Microsoft.ML.OnnxRuntime`; streams 20–40 ms PCM chunks.
- `YandexSpeechKitClient` — optional premium path; bidirectional gRPC `StreamSynthesis`.
- Both expose the same `ITtsBackend` interface.

### 3.7 Audio
- `PriorityAudioQueue` — sorts pending utterances by priority; preempts running utterance with 10–20 ms linear fade-out.
- `NAudioPlayer` — `WasapiOut` shared mode, 48 kHz stereo, 20 ms buffer.

### 3.8 Overlay
- Avalonia 11+ application running on the same process as `SimCoach.App` (in-process).
- `OverlayWindow.axaml` — transparent, topmost, click-through via Win32 `WS_EX_TRANSPARENT` interop.
- Bindings to `OverlayViewModel` that re-renders at 30 Hz from in-process channels.

### 3.9 App
- `Program.cs` — Generic Host bootstrapping; reads `appsettings.json` + `secrets.json`.
- Settings UI for layout, model, voice, hotkeys, mute, references browser.

## 4. Security & Privacy

- **No DLL injection** — overlay is a separate window, not a game hook.
- **Read-only** access to game shared memory; no writes to game memory.
- **Local-first**: raw telemetry, MCAP recordings, reference laps stay under `%LOCALAPPDATA%/SimCoach/`.
- **Only Gold artifacts leave the machine**, addressed at user-chosen OpenRouter endpoint and (optionally) Yandex SpeechKit.
- **Secrets**: `secrets.json` excluded from git via `.gitignore`; user provides their own OpenRouter API key.
- **No analytics/telemetry to vendor** until phase 7 (opt-in only).

See `privacy.md` for the full data-flow map.

## 5. Performance Budget

| Subsystem | Budget |
|---|---|
| ACC SHM read | < 1 ms per frame at 333 Hz |
| Pipeline compute | < 2 ms per frame |
| MCAP write | < 1 ms per frame (async buffer) |
| LLM in-corner request | ≤ 1 s p50, ≤ 2 s p95 |
| TTS first audio | ≤ 200 ms |
| Overlay frame budget | < 2 ms at 60 Hz |
| Total process RAM | ≤ 600 MB steady state |
| Total process CPU (idle/race) | ≤ 12% on a 6-core CPU |

## 6. Deployment

- `dotnet publish -r win-x64 --self-contained` produces a single tree.
- Velopack for installer + auto-update.
- ONNX model `v5_ru.onnx` ships alongside binaries (~50 MB).
- Total installer ≤ 200 MB.
- No external runtime dependency beyond Windows 10 21H2+.

## 7. Cross-platform Note

Project is C#/.NET 9 — buildable from macOS for non-WPF projects, fully runnable only on Windows. Avalonia 11 (instead of WPF) enables overlay project to build on macOS too, making solo dev practical without a Windows VM. See `adr/0002-overlay-avalonia-not-wpf.md`.
