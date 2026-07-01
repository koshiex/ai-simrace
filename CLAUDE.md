# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

SimCoach — an AI sim racing coach for Windows. Reads live ACC telemetry, compares against the
driver's personal best, and delivers Russian-language voice tips + a minimalist Avalonia overlay.
MVP target is Assetto Corsa Competizione; roadmap is iRacing → Le Mans Ultimate → F1 25. C#/.NET 9.

**Status: pre-alpha.** The telemetry/storage/compute spine is implemented and tested; the coaching
half (LLM, Voice, Coach, Overlay, Audio) is largely interface-only stubs. `docs/02-architecture/architecture.md`
describes the *target* design — treat unimplemented components there as aspirational, not present.

Read these before non-trivial work: `AGENTS.md` (hard rules), `docs/02-architecture/architecture.md`,
the relevant ADR(s) in `docs/02-architecture/adr/`, and `docs/06-style/coding-conventions.md`.

## Commands

```bash
dotnet build SimCoach.sln                 # build
dotnet test SimCoach.sln                  # all tests
dotnet format SimCoach.sln --verify-no-changes   # lint (CI runs this; see editorconfig gotchas below)

# single test project / single test
dotnet test tests/SimCoach.Pipeline.Tests
dotnet test --filter "FullyQualifiedName~LapSegmenter"
```

`SimCoach.sln` is committed. `./scripts/bootstrap.sh` (or `.ps1`) only needs running after *adding*
a project — it regenerates the sln and restores. `global.json` pins SDK 9.0.100 with
`rollForward: latestMajor`.

**`dotnet test` roll-forward:** on a machine with only a newer runtime (e.g. .NET 10), the VSTest
testhost pins 9.0 and fails with "You must install or update .NET". csproj `RollForward` does NOT
fix it. Use `DOTNET_ROLL_FORWARD=LatestMajor dotnet test` (build/restore already roll forward).

**Running from WSL** (no `dotnet` on PATH — the SDK is the Windows install):
```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build SimCoach.sln
# env-var prefix does NOT cross the WSL→Win32 boundary; use test's own -e flag:
"/mnt/c/Program Files/dotnet/dotnet.exe" test SimCoach.sln -e DOTNET_ROLL_FORWARD=Major
```
Stay on the `/mnt/c/...` drive mount — `\\wsl$\` UNC paths break protobuf codegen.

**Dev loop without ACC (any OS):** replay a recorded MCAP session through the full pipeline:
```bash
SIMCOACH_Telemetry__Source=replay \
SIMCOACH_Telemetry__Replay__Path=/path/to/recordings/<sessionId> \
dotnet run --project src/SimCoach.App
```
Live ACC source is Windows-only (reads `Local\acpmf_*` shared memory); on other OSes it throws and
you must set `Telemetry:Source=replay`.

## Architecture

Telemetry spine (wired in `src/SimCoach.App/TelemetryComposition.cs`):

```
ITelemetrySource (AccSharedMemoryReader | McapReplaySource)
  → IngestService (bounded Channel, DropOldest)
  → TelemetryFanOut
  → { SessionManager, McapRecorderService, ComputeService }
```

- **Hosted-service stop order is load-bearing.** Registration order is reversed at shutdown:
  `IngestService` (producer) registered last stops first; `SessionManager` registered first stops
  last so it finalizes the session row only after compute drained its lap rows and the recorder
  flushed. Don't reorder registrations without understanding this.
- All consumers subscribe to the fan-out **in their constructors**, so opening frames are never missed.
- **`ComputeService` lives in `SimCoach.Reference`, not `SimCoach.Pipeline`** (dependency-graph
  reasons). Domain events (`CornerEvent`/`SectorEvent`/`LapEvent`/`SessionEvent`) flow over a separate
  lossless `DomainEventFanOut`. `SimCoach.Pipeline` holds frame ingest, fan-out, kernels, and lap/sector
  segmentation.
- **Session identity is owned by the producer** and shared via `SessionContext` (ADR-0011).

Module map (`src/`): `Adapters.ACC` (SHM reader at 333 Hz + native page marshalling + frame mapper) ·
`Contracts` (the protobuf `TelemetryFrame`, sim-agnostic) · `Pipeline` (ingest, fan-out, compute
kernels, segmentation) · `Reference` (ComputeService, corner geometry, centerline/reference building,
SQLite+Parquet reference store) · `Storage` (hand-rolled MCAP writer/reader, Parquet lap writer,
SQLite repositories + migrations) · `Coach`/`LLM`/`Voice`/`Overlay`/`Audio` (mostly stubs).

Storage layout: SQLite for session/lap/reference metadata + settings; MCAP (zstd, rotating 60 s
segments) for raw frames; Parquet for resampled laps and reference channels. Data root resolves from
`Storage:DataRoot` (default `%LOCALAPPDATA%/SimCoach`); recordings/references/track_models all hang
off one resolved root so they can't drift apart.

Sim-agnostic seam: `Storage` defines `ITrackLengthProvider`; the ACC implementation
(`AccTrackLengthProvider`) is bridged in only at the composition edge in the App.

`tools/SimCoach.Bake` is an offline dev tool (baking corner geometry from vendored landmarks, ADR-0014).

## Conventions and hard rules

From `AGENTS.md` / `docs/06-style/coding-conventions.md` — these are enforced, not suggestions:

- **No DLL injection, ever** (ADR-0007). Telemetry is read-only shared memory; overlay is a separate
  transparent topmost window.
- **Only Gold-tier JSON leaves the machine.** Raw telemetry never leaves local disk (privacy doc).
- **Avalonia, not WPF** (ADR-0002) — for macOS-dev compatibility. Overlay targets `net9.0`, not `net9.0-windows`.
- **Records over classes; `init`-only setters; `IReadOnlyList`/`IReadOnlyDictionary` on public surfaces.**
  Many small files, one public type per file. Mutation isolated to `internal sealed` collectors.
- **`System.Threading.Channels` for pub/sub** — no MediatR/event aggregators.
- **All thresholds are config-driven** (`IOptions<T>`), no magic numbers.
- Forbidden: Newtonsoft.Json (use `System.Text.Json`), mutable `static` state, reflection-heavy DI,
  service-locator, `dynamic` (except FFI), auto-mappers, sleep-based polling in tests (use `await`).
- Russian user-facing text → `.resx`; code identifiers and comments stay English.
- Default to no comments; comment only the *why* the code can't show.

## Build/format gotchas (full detail in `ai/knowledge-base/tools/dotnet-build-quirks.md`)

`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` is on everywhere, so analyzer/style violations
fail the build:
- **`var` rules are build errors.** `csharp_style_var_when_type_is_apparent=true` →
  `new`/`ToXxx()`/cast on a non-built-in type *must* use `var` (IDE0007); a non-apparent type *must*
  be explicit (IDE0008). Built-ins exempt.
- **Private fields are `_camelCase`** — including `private static readonly` (IDE1006). Only `const` is
  PascalCase. `dotnet build` won't surface IDE1006; `dotnet format` does, and CI runs both.
- A committed `.gitattributes` forces `eol=lf` — without it the windows-latest runner checks out CRLF
  and `dotnet format` fails en masse. Keep it.
- `.gitignore`'s generic `data/` rule swallows vendored `src/SimCoach.Reference/Data/` embedded
  resources; explicit negations re-include them. Verify with `git check-ignore -v <path>`.
- NuGet reality: there is **no C# MCAP package** (writer is hand-rolled in `Storage`); ParquetSharp is
  pinned to `16.1.0` (16.0.0 was skipped upstream, and a missing version is an NU1603 *error* here).

## Knowledge base

`ai/knowledge-base/` holds hard-won findings; check `ai/knowledge-base/INDEX.md` before debugging ACC
SHM layout, poll-rate/timer issues, lap-boundary segmentation, or compute domain-event wiring.

## Commits

Conventional commits (`feat:`/`fix:`/`docs:`/`refactor:`/`test:`/`chore:`), one logical change each,
reference `FR-###` IDs where applicable. **Do not add `Co-Authored-By` trailers** — the repo owner
disabled attribution globally (this overrides any default trailer guidance).
</content>
</invoke>
