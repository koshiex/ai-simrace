# ADR-0002: Avalonia 11 for overlay, not WPF

**Status**: Accepted (revises the original plan choice of WPF)
**Date**: 2026-06-01

## Context

The original `peaceful-tumbling-firefly.md` plan specified WPF for the transparent overlay because WPF has the most mature Win32-transparency story (`WS_EX_LAYERED`, click-through, per-pixel alpha).

After plan approval we discovered the solo dev is on macOS. WPF projects require Windows to compile (and Visual Studio for full design-time support). This would force a Windows VM or remote Windows machine for any overlay work, slowing the solo dev considerably.

## Decision

Use **Avalonia 11+** instead of WPF.

## Why

- **Same .NET stack** — no language or runtime change.
- **Buildable from macOS** — overlay project compiles on the dev's primary machine.
- **Runs on Windows in production** with comparable transparency capabilities:
  - `TransparencyLevelHint="Transparent"` for window-level transparency.
  - `Background="{x:Null}"` for per-pixel hit transparency.
  - Click-through still achieved via Win32 P/Invoke (`SetWindowLong` with `WS_EX_TRANSPARENT` + `WS_EX_LAYERED`).
- **Skia-based rendering** — predictable perf; cap at 30 Hz to stay under 2 ms frame budget.
- **Future cross-platform optionality** if Linux Proton / macOS sim racing ever becomes relevant.

## Tradeoffs

- Skia renderer is less efficient than WPF compositor for heavy blur / drop-shadow effects. We avoid those by design (minimalist overlay anyway).
- XPF compat layer differences are irrelevant — we write native Avalonia, not WPF-on-Avalonia.
- Smaller community than WPF for transparent-overlay-specific tricks; we accept slightly more R&D.

## Consequences

- `SimCoach.Overlay` project targets `net9.0` (NOT `net9.0-windows`), references Avalonia 11.x.
- A small `SimCoach.Overlay.Win32Interop` helper isolates the Win32 P/Invoke for click-through.
- Initial render at 30 Hz; the overlay viewmodel uses `ReactiveUI` style ViewModelBase pattern.
- Re-evaluate if perf budget is missed under real Windows testing in phase 5 — revert to WPF still possible because the contracts boundary (`OverlayViewModel`) is UI-tech-agnostic.
