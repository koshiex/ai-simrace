# Adding a coach action or Gold field — the sync surface

A new corner action or a new Gold scalar it reads touches several files that MUST stay in sync;
mismatches surface as loud test failures (which is the point) but the failure messages don't name every
file, so know the surface up front.

## Adding a corner action to `actionRegistry.json`

- **`(phase, rank)` must be GLOBALLY unique**, not unique-within-phase. Two exit actions with the same
  rank throw `InvalidOperationException: duplicate priority CoachPriority { Phase = Exit, Rank = 41 }` at
  `ActionRegistry.Load()` (caught by `ActionRegistryLoadTests.Loads_embedded_registry_without_throwing`).
  Check the existing ranks for the phase (`grep '"phase": "exit", "rank"'`) before picking one — ranks are
  sparse and non-contiguous (e.g. 40, 41, 42, 200–204, 300–302, 900+), so scan, don't assume `max+1`.
- **`ActionRegistryLoadTests.Loads_the_authored_action_count` hardcodes the total action count** — bump it
  by the number of actions you added, or it fails.
- Every field a `when`/`param` clause references must resolve through the per-cadence Gold view (below).

## Adding a Gold scalar a clause references (e.g. a new `CornerEvent` field)

Four in-sync edits, enforced by `CoachStartupValidator` (#4 check) + drift tests:

1. **`GoldCornerEvent`** — add the field. New fields go as **`init` members** (like `CornerNameRu`), NOT
   positional record params, so the positional shape (and all `new GoldCornerEvent(...)` fixtures) don't
   shift. Reference-relative fields are nullable and left `null` when there is no reference.
2. **`GoldArtifactBuilder.BuildCorner`** — populate it (in the `{ ... }` init block), gating on `hasRef`
   for reference-relative fields.
3. **`CornerGoldView.TryGetNumber/TryGetBool/TryGetString`** — add a `case "<field_name>":`.
4. **`GoldFieldNames._corner`** (the static catalog) — add the field name string. The catalog and the view
   switch must match exactly; `GoldFieldNamesTests` guards the drift.

Then the gotcha that fails ~70 tests at once:

- **`CoachStartupValidator.SampleView(Corner)` builds a fully-populated `GoldCornerEvent` with a
  positional ctor** — the new `init` field defaults to `null`, so the #4 "every registry field resolves"
  check fails with `Action '<id>' references Gold field '<field>' that does not resolve for cadence
  'Corner'` for EVERY action, cascading into the `CoachStartupValidatorTests`. Set the new field
  **non-null** in that fixture's `{ ... }` init block.
- Nullable reference-relative fields also belong in `GoldHasReferenceDropTests` (the
  `NotContain(...)` list that asserts they are omitted from the JSON without a reference).

RU phrase text stays in `phrase_template_ru` / `.resx`; the `hint_ru`/`hint_en` ride the registry entry and
auto-appear in the prompt menu (no prompt-file edit needed).

## A `when`-clause gate on a kernel score must clear the kernel's attainable ceiling

An action gated on a computed `[0,1]` score can ship **dead** if the gate sits above the score the
kernel can actually reach for the target car class — the registry loads, the Gold field resolves, all
tests pass, and the action simply never fires in-game. `brake_lockup_score` is the worked example: the
kernel **attenuates any ABS-caught lock** (`BrakeLockupKernels`, ABS branch caps at `raw * AbsAttenuation`,
`AbsAttenuation = 0.35`), so on the MVP target (ACC GT3, ABS on) the deepest-lock frame reads **≤ 0.35** by
construction — a `brake_lockup_score gt 0.4` gate is unreachable there. When you add or retune a
kernel-gated action, verify the gate against the kernel's worst-case attainable value for the primary car,
not against `1.0`. Guard it with a **reachability test** (see
`tests/SimCoach.Coach.Tests/BrakeLockupActionReachabilityTests.cs`): load the registry, extract the gate
from the `when` clause, compute the kernel score for the saturating fixture, assert `score > gate`. A
range-only `score in [0,1]` assertion proves nothing here — it passes for any constant.

## A lap-scoped boolean claim must come from SUSTAINED exposure, not an instantaneous max

If an action asserts a *state* ("тормоза перегреты", "шины перегреты", "abused"), the flag behind it must
measure how LONG the channel sat outside its band — not whether the peak ever crossed it once. A max-over-
frames flag latches for the whole lap on a single sample, so the coach states something the driver can see
is false on the in-game HUD, which is the fastest way to burn trust. Worked example: `ThermalKernels`
originally did `BrakeOverheat = maxBrake > 700`. On a real Monza lap the brakes ran a **414 °C median /
637 °C p95** and touched **701 °C for 67 ms (24 of 159 474 frames = 0.015 %)** at the heaviest stop — one
degree over, for a sixtieth of a second — and the debrief announced "brakes overheated" while the HUD brake
widget was still blue (cold). Two independent defects, fix both:
1. **Wrong statistic** — replace the instantaneous max with an exposure ratio (fraction of the
   temperature-carrying frames above the band; `MinOverheatFractionOfLap = 0.02`). Keep the peak as a
   reported metric — it is informative, it just is not the flag.
2. **Threshold inside the operating window** — 700 °C is normal for GT3 carbon at Monza (p95 = 637). An
   "abuse" band must sit above the normal envelope (raised to 800 °C), not inside it. Sanity-check any
   thermal/limit threshold against a real lap's distribution before trusting it.

Verify a threshold change against recorded data, not intuition: `tools/SimCoach.GroundTruthDump` emits
`max_brake_temp_c` / `max_tyre_temp_c` per frame for exactly this (brake temps exist ONLY in the MCAP —
`laps.parquet` has no brake-temp column), so you can compute the exposure ratio for both the old and new
band before/after the change. Guard with a test that a single-frame spike reports the peak but does NOT
raise the flag (`Thermal_transient_spike_reports_the_peak_but_does_not_flag_overheat`).
