# ADR-0009: Distribute SimCoach.App as a self-contained single-file exe from CI

**Status**: Accepted
**Date**: 2026-06-21

## Context

Running the app on a Windows rig (the only place ACC lives — see ADR-0007) meant
cloning the repo and `dotnet build`/`dotnet run` by hand. That has friction we hit
repeatedly during Phase 1 verification:

- The dev rig has no git in `cmd`/no checkout workflow; building from the WSL UNC path
  breaks protobuf codegen (`Could not make proto path relative`), so the repo must live
  on a native Windows path.
- Only .NET 6/8/10 runtimes were installed, not 9 — tests needed `DOTNET_ROLL_FORWARD=LatestMajor`.
- CI already **built and tested** on `windows-latest` (`ci.yml`) but produced nothing you
  could download and run.

We want: download one artifact, run it, no SDK/runtime install, no checkout.

## Decision

**A CI job publishes `SimCoach.App` as a self-contained, single-file `win-x64` executable and
uploads it as a downloadable artifact** (`.github/workflows/publish.yml`, artifact
`SimCoach-win-x64`).

- **Self-contained** (`--self-contained true`): the .NET 9 runtime is bundled — nothing to
  install on the target, and the roll-forward problem disappears.
- **Single file** (`PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true`):
  one `SimCoach.App.exe`; native dependencies (ONNX Runtime, Skia/Avalonia, ParquetSharp,
  SQLite) are self-extracted to a temp dir on first run.
- **No trimming**: the app relies on reflection (DI, Avalonia, configuration binding) that the
  IL trimmer would silently break. Size is traded for correctness.
- **`appsettings.json` ships loose next to the exe**, not embedded — it is the config source of
  truth the user edits (OpenRouter key, voice model path; see FR-073). The payload is exactly
  `SimCoach.App.exe` + `appsettings.json`, nothing else.
- **Trigger: pull requests into `main` only**, for now. Every PR leaves a build under its checks.

## Why

- **Zero-install run**: self-contained + single-file is the shortest path from "open a PR" to
  "double-click an exe on the rig". No SDK, no runtime, no checkout, no UNC-path codegen break.
- **Reuses the existing Windows CI**: the toolchain that already builds/tests on `windows-latest`
  also publishes — no new cross-compile concerns.
- **`win-x64` only is sufficient**: ACC is Windows-only and we never inject into the game
  (ADR-0007), so a single Windows RID covers every target the app can run on.
- **Config stays a file, not embedded**: matches the "settings live in JSON, GUI just edits it"
  decision (FR-073); the user must be able to edit config without rebuilding.

## Tradeoffs

- **Size**: the exe is ~60 MB (bundled runtime + native libs), artifact zip ~51 MB. Acceptable
  for an occasional download; trimming could shrink it but would break reflection-heavy code.
- **First-run latency**: native libs self-extract to temp on first launch (one-off), and some
  AV/SmartScreen may scan the unsigned binary. The exe is **not code-signed** → expect a
  SmartScreen "unknown publisher" prompt. Signing is deferred (no cert yet).
- **No stable download URL**: PR artifacts expire (90-day default) and require an open PR — there
  is no permanent release link. Acceptable pre-1.0; a `v*` tag → GitHub Release path is the
  obvious upgrade when public releases start.
- **PR-only trigger** means no on-demand or `main`/tag builds today; both were intentionally
  dropped to keep scope minimal and are a few lines in `on:` to restore.

## Consequences

- New workflow `.github/workflows/publish.yml`; `ci.yml` (build + test) is unchanged.
- The build must remain **trimming-free**: do not add `PublishTrimmed`/`TrimMode` without
  validating Avalonia + DI + config binding survive.
- `docs/05-implementation/windows-live-verification.md` points at the artifact as the fast path
  instead of "clone and build".
- When stable/public downloads are needed: add `workflow_dispatch` and a `v*` tag trigger that
  attaches the zip to a GitHub Release (was prototyped, then removed here as out of scope).
