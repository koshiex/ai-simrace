# ADR-0007: No DLL injection, no game-process hooks

**Status**: Accepted
**Date**: 2026-06-01

## Context

There are two common ways to put an overlay on a fullscreen game:
1. **External**: a separate process that opens a transparent topmost window. Requires the game to run in borderless / fullscreen-optimisation mode.
2. **Internal**: a DLL injected into the game process that hooks DirectX `Present` to draw atop the swapchain.

The internal approach gives sub-millisecond perf and works on true fullscreen exclusive (FSE). But it triggers anti-cheat: iRacing's Easy Anti-Cheat in particular is known to flag and ban accounts that load unauthorized DLLs into the game process.

## Decision

**SimCoach never injects code into the game process.** Overlay is a separate transparent topmost window. Telemetry is read via official shared-memory APIs (ACC `acpmf_*`, iRacing `IRSDKMemMapFileName`, LMU plugin's exported file mapping, F1 25 UDP).

## Why

- **Anti-cheat safety** across ACC, iRacing, LMU.
- **Future-proofing**: anti-cheat policies evolve toward stricter integrity checks. Living outside the process is durable.
- **Simpler code**: no DXGI hooking, no signature scanning, no reverse engineering.
- Performance is acceptable: a transparent topmost window in Windows 11 adds ~0.3–0.8 ms of DWM composition.

## Tradeoffs

- True FSE (rare in 2026) cannot show our overlay. We document the borderless requirement in onboarding. ACC/iRacing/LMU default to borderless or FSO already; F1 25 supports both.
- ACC's anti-cheat is permissive but iRacing's is strict. We MUST validate the no-injection guarantee with iRacing pre-Phase-2-release (run under EAC, confirm no DLL load events).

## Consequences

- No P/Invoke into the game process; only into Windows APIs we own (`SetWindowLong`, `MemoryMappedFile`, NAudio's WASAPI).
- No use of MinHook, Detours, EasyHook, or similar.
- Documented in `architecture.md` security section.
