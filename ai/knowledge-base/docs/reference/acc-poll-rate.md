# ACC poll rate — Windows timer resolution caps emit at ~64 Hz

The ACC physics page updates fast — **333 Hz is the nominal Kunos figure, but the page's real
`packetId` advance rate measured ~400 Hz on hardware** (BMW M4 GT3, Spa). `AccSharedMemoryReader`
polls on a dedicated thread and dedups on `packetId`, so the emitted frame rate equals
min(poll rate, page update rate). This card records why the poll rate silently collapsed and how
it's fixed — not visible from the code.

Sanity check that dedup works: with ~1000 Hz polling we still emit only ~400 Hz (= the page's
real rate), not ~1000 Hz. If emit ever tracks the poll rate, dedup is broken and frames are dupes.

## Symptom (B7 live verification, real hardware)

Steady-state `Segment N finished` logged **~64–65 Hz**, not 333 Hz (first segment even lower,
~25 Hz, during warm-up). No dropped frames. 65 Hz ≈ `1000 / 15.6` — the smoking gun.

## Root cause

The poll loop waits `PollInterval` (1 ms) between ticks via `ct.WaitHandle.WaitOne(delay)`.
On Windows, waits are quantized to the **system timer tick — ~15.6 ms by default**, so a
requested 1 ms wait actually sleeps ~15.6 ms. Each poll is then > 3 ms apart, so every poll sees
a fresh `packetId` and emits → the emit rate collapses to the *poll* rate (~64 Hz), not the
game's 333 Hz. The bottleneck is the timer, not the mapper/channel (0 frames dropped confirms it).

## Fix

Raise the multimedia timer resolution to 1 ms for the lifetime of the poll loop via
`timeBeginPeriod(1)` / `timeEndPeriod(1)` (winmm.dll), wrapped in the RAII helper
`WinTimerResolution` (`using var` at the top of `PollLoop`). Then `WaitOne(1)` ≈ 1–2 ms → poll
~500–1000 Hz → dedup → emit at the page's real ~400 Hz. No-op off Windows (the page source is
Windows-only anyway). Confirmed on hardware: steady ~399–400 Hz, 0 dropped frames.

- `timeBeginPeriod` must be balanced by `timeEndPeriod` — hence the `IDisposable`.
- Process/system-wide setting with a tiny power cost; standard for sim-telemetry tools and
  negligible next to a running game.
- Plan B if it ever proves insufficient: busy-spin with `PollInterval = 0` (the loop already
  falls back to `Thread.Yield()` for a zero interval) — pegs a core, so prefer the timer bump.

## Implementation notes

- `WinTimerResolution` uses `[LibraryImport]` (not `[DllImport]`) because `TreatWarningsAsErrors`
  promotes SYSLIB1054 to an error. `LibraryImport` needs `partial` methods/class **and**
  `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the csproj (the generator emits unsafe stubs).
- Native exports are camelCase (`timeBeginPeriod`); use PascalCase method names + `EntryPoint`
  to satisfy the IDE1006 naming rule (also a build error here).
