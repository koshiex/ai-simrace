# Detailed Plan — Phase 0 Closeout + Phase 1 (ACC Telemetry + MCAP Capture)

Expands `implementation-plan.md` Phase 0 (remaining items) and Phase 1 into ordered,
testable steps. Status legend: `[ ]` todo, `[x]` done.

---

## Stage A — Phase 0 Closeout (prerequisite, ~half a day)

Goal: `SimCoach.sln` restores, builds, formats clean, tests pass — locally and in CI.

- [x] **A1. SDK pin fix.** `global.json` requires `9.0.100` with `rollForward: latestFeature`,
  which rejects the installed SDK 10.0.300. Change to `rollForward: latestMajor`.
  Target framework stays `net9.0`; CI keeps `dotnet-version: 9.0.x`.
- [x] **A2. Remove `Mcap.Core` package reference** from `SimCoach.Storage.csproj` and
  `Directory.Packages.props`. Verified 2026-06-10: no MCAP C# package exists on NuGet.
  Per the risk register (ADR-0003 mitigation) we hand-roll a minimal MCAP writer in Phase 1.
- [x] **A3. Bootstrap.** Run `scripts/bootstrap.sh` → generate `SimCoach.sln`, restore.
  Commit the `.sln` (IDE-friendly; CI's bootstrap skips creation when present).
- [x] **A4. Build green.** `dotnet build` with `TreatWarningsAsErrors` — fix whatever falls out
  (analyzer warnings, NAudio/Avalonia TFM warnings on macOS, generated protobuf code).
- [x] **A5. Protobuf smoke test.** Round-trip `TelemetryFrame` serialize → parse in
  `SimCoach.Pipeline.Tests` — proves Grpc.Tools codegen is hooked correctly.
- [x] **A6. `dotnet format --verify-no-changes`** clean; `dotnet test` green.
- [x] **A7. Tick Phase 0 checkboxes** in `implementation-plan.md`; commit.

Definition of done: fresh clone + `bootstrap.sh` + `dotnet build && dotnet test` succeeds
on macOS and on CI (windows + macos matrix).

---

## Stage B — Phase 1: ACC Telemetry + MCAP Capture

Goal: with ACC running on Windows, the app records normalized telemetry to rotating MCAP
segments; on any OS, a replay source re-emits a recorded session through the same pipeline.

Dependency order: B1 → B2 (B3 parallel) → B4 → B5 → B6 → B7.

### B1. ACC shared-memory struct layouts (`Adapters.ACC`) — done (real Windows SHM dumps still pending)

- `AccPhysicsPage`, `AccGraphicsPage`, `AccStaticPage` — `[StructLayout(LayoutKind.Sequential,
  CharSet.Unicode, Pack = 4)]`, fields per the official ACC shared-memory documentation
  (kunos `SPageFilePhysics` / `SPageFileGraphic` / `SPageFileStatic`).
- Strings are fixed-size UTF-16 char arrays; arrays (tyres etc.) fixed length 4 — use
  `unsafe fixed` buffers or `[MarshalAs(UnmanagedType.ByValArray)]`.
- **Tests:** `Marshal.SizeOf` golden values + field-offset asserts (`Marshal.OffsetOf`)
  against the documented C++ layout; parse a binary fixture page and assert known values.
- **Fixtures:** synthetic binary pages built in test code first; real SHM dumps captured on a
  Windows machine later (drop into `tests/SimCoach.Adapters.ACC.Tests/Fixtures/`).

### B2. `AccSharedMemoryReader : ITelemetrySource` — done (real-rig verification deferred to B7)

- Opens `Local\acpmf_physics`, `Local\acpmf_graphics`, `Local\acpmf_static` via
  `MemoryMappedFile.OpenExisting`.
- Dedicated background thread, busy-poll at 333 Hz; new frame detected via `packetId` change.
  Torn-read guard: read `packetId`, copy page, re-read `packetId` — retry if changed (seqlock).
- Reconnect loop: if ACC is not running (`FileNotFoundException`), retry every 1 s; transparent
  reconnect on game restart (per `ITelemetrySource` contract).
- Windows-only code guarded with `OperatingSystem.IsWindows()` / `[SupportedOSPlatform]`;
  bridged to `IAsyncEnumerable<TelemetryFrame>` via an internal bounded `Channel`.
- **Tests (cross-platform):** poll-loop logic extracted behind an `IAccPageSource` seam so the
  seqlock/dedup/reconnect logic is unit-testable with a fake page source; real MMF path is a
  thin adapter verified manually on Windows.

### B3. `AccFrameMapper` (pure function) — done

- Native structs → `TelemetryFrame`: unit conversions (km/h → m/s, normalized pedals, gear
  offset — ACC gear 0=R,1=N,2=first → contract -1/0/1), `[FL,FR,RL,RR]` ordering.
- `track_id` / `car_id` normalization: lowercase + known-alias dictionary.
- `weather_bucket` derivation from air/track temp + rain intensity → `dry-cool | dry-warm |
  damp | wet` (thresholds as named constants).
- **Tests:** golden mapping on fixture pages; edge cases (gear=0 reverse, wet bucket cutoffs).

### B4. `IngestService` (`Pipeline`) — done

- `Channel.CreateBounded<TelemetryFrame>(256)` with `BoundedChannelFullMode.DropOldest`.
- `BackgroundService`: consumes `ITelemetrySource.ReadAsync`, writes to channel; fan-out
  broadcaster so N consumers (recorder now, compute in Phase 2) each get every frame.
- Dropped-frame counter, logged at most once per 10 s.
- **Tests:** backpressure drops oldest; fan-out delivers to all subscribers; cancellation clean.

### B5. Minimal MCAP writer (`Storage`) — done (no summary section: `mcap doctor` passes, `mcap cat` needs the zstd+summary follow-up)

- Hand-rolled per the [MCAP spec](https://mcap.dev/spec): magic, `Header`, `Schema`
  (protobuf descriptor bytes), `Channel`, `Message` records, `Chunk` + CRC32, `Footer`.
  Iteration 1: `compression: ""` (none); zstd via `ZstdSharp.Port` as a follow-up step.
- `McapRecorderService` (`BackgroundService`): subscribes to ingest fan-out, rotates segments
  every 60 s, files at `%LOCALAPPDATA%/SimCoach/recordings/<sessionId>/segment-NNN.mcap`
  (path from config; cross-platform base dir via `Environment.SpecialFolder.LocalApplicationData`).
- Matching minimal `McapReader` (needed for replay + tests).
- **Tests:** write N frames → read back → payload byte-identical; segment rotation at boundary;
  (optional, CI-skippable) validate output with the `mcap` CLI if present.

### B6. `McapReplaySource : ITelemetrySource`

- Reads MCAP segments, re-emits frames honoring original inter-frame timing
  (`speed` multiplier in config; `0` = as fast as possible for tests).
- This is the macOS dev loop and the test harness for Phase 2 compute work.
- **Tests:** replayed stream equals recorded stream (frame count, payloads); timing multiplier.

### B7. Wiring + end-to-end

- `Program.cs`: register source by config — `Telemetry:Source = "acc" | "replay"` —
  plus `IngestService` and `McapRecorderService`. `appsettings.json`: recordings path,
  segment seconds, replay file, speed.
- **E2E test:** replay fixture → ingest → recorder → output MCAP messages byte-identical
  to input (the Phase 1 checklist's "byte-identical events" criterion).
- Manual verification on a Windows machine with ACC: segments appear, frame rate ≈ 333 Hz,
  reconnect survives game restart.

Definition of done: Phase 1 checklist in `implementation-plan.md` fully ticked; CI green;
recorded fixture session committed for Phase 2 use.

---

## Decisions taken in this plan

| Decision | Rationale |
|---|---|
| Hand-roll MCAP writer | No C# MCAP package on NuGet (checked 2026-06-10); spec is simple; risk register pre-approved this path |
| No compression in MCAP v1, zstd later | Cuts scope; format allows `compression: ""`; reader/writer stay symmetric |
| `rollForward: latestMajor` in global.json | Dev machine has SDK 10.x; TFM stays net9.0 so output is unchanged; CI still pins 9.0.x |
| `IAccPageSource` seam in reader | SHM is Windows-only; seam keeps seqlock/reconnect logic unit-testable on macOS/CI |
| Replay source in Phase 1 (not later) | Primary dev machine is macOS; without replay, nothing past B2 is locally testable |
