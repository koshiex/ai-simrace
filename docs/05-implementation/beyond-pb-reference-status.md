# Beyond-PB reference — investigation status & map

Orientation doc (resume anchor). The problem, every theory we tested, its verdict, and where we are now.
Links to the detailed logs. Keep this current — it is the single place to re-orient after a context reset.

## The problem

SimCoach coaches a driver who is **near their own ceiling**. Any reference built from the driver's OWN data
tends to be **self-referential** — it reproduces the ceiling instead of a target beyond it. We need a
trustworthy "faster than your PB" reference (a TIME target and ideally a LINE), sim-agnostic where possible.

## Theories tested — verdicts

| # | Theory | Verdict | Why / detail |
|---|--------|---------|--------------|
| A | Physics friction-envelope "ideal line" from the driver's OWN telemetry | **FALSIFIED ×2** | Naive (µ from one corner reproduces its ceiling); then on MEASURED g too — the beyond-PB delta is envelope-*independent* point-mass/median-line geometry optimism (~26 km/h), the only non-circular term is mean −5.9 km/h buried under noise. `ideal-line-reference-research.md` §4–6. |
| B | Capture a reference by recording ACC **shared memory during a replay** | **DEAD** | ACC blanks physics + freezes coords in replay; menu replay = SHM off entirely. `AllowReplayCapture` flag works but has no data behind it. `ai/knowledge-base/docs/reference/acc-shared-memory-layout.md`. |
| — | MoTeC `.ld` **file import** (alien lap) | **NO LINE / not shippable** | `.ld` has NO world coords/spline (dead-reckoned map only) → can't anchor a LINE; no redistributable alien corpus (paid personal-license). Only a distance-axis TIME reference from a lap the user personally owns. Firsthand-confirmed on a real Monza `.ld` (55 channels, zero position). `ideal-line-reference-research.md` §7. |
| B′ | Capture a faster **opponent's** live line in a race | **MISFIT** | Solo hotlapper doesn't race. (Opponents' world coords ARE in live SHM — parked for a future race mode.) |
| — | ACC **`.ghost`** file → external LINE | **DECODER READY, needs alien specimen** | Format reverse-engineered + verified (1.15 m to our own line). Real world-LINE source in OUR frame. But specimens so far are slow drives; needs a ghost harvested from a replay **focused on a fast alien**, and the clock pinned. Line only, no pedals. `acc-ghost-format-re.md`. |
| **M46** | **Own-optimal** = stitch the driver's best-ever sectors into a synthetic lap | **GO — building next** | Measured Monza gap **1.044 s** (Σ best sectors vs PB, 14 clean laps). Non-circular (every sub-target physically driven). Own data, ships first. `m46-optimal-reference-plan.md`. |

Theory-loop (ultracode) also generated and **rejected** as circular/impractical: WR-video telemetry harvest,
aids-saturation oracle, geo-registering foreign dead-reckoned lines, extracting grip from own track
excursions, boundary-recon width audit. Survivors that remain as future options: perturbation-probe
staircase and externally-seeded hypothesis stints (both need a matured Coach voice/overlay channel, currently
a stub), community ghost-file exchange (= the ghost track above), and BYO `.ld` TIME-only import.

## Where we are now

- **Next PR = M46 (own-optimal, per-sector).** Blueprint-only until owner greenlights coding. Plan +
  commit sequence + decided knobs (per-sector; `MinOptimalGainMs` user-facing; debrief-only UX first) in
  `m46-optimal-reference-plan.md`. Open owner decisions: outlier tolerance, recency policy, Gold sign-off.
- **Ghost/alien-line = parallel research follow-up**, not on M46's critical path. Needs (1) a ghost harvested
  from a replay focused on a genuinely fast alien, (2) the timestamp clock pinned against a known-duration
  ghost, (3) the 6 undecoded input bytes/record cracked (for pedals). `acc-ghost-format-re.md`.
- **Always-safe fallback:** relative coaching (measured g → brake-release/throttle-timing deltas vs PB) and
  the existing PB TIME + self-median LINE references already shipped.

## Branch / artifact state

- Working branch `feat/replay-telemetry-capture` (stacked on unmerged PR-A/#34). Contains: the dumper
  g-force/world-pos extension, opt-in `AllowReplayCapture`, `tools/SimCoach.ShmProbe` (raw SHM diagnostic),
  and all research docs. PR-A (#34, the M34/M38/reference-snapshot work) is still open on top of `main`.
- Key tools: `tools/SimCoach.ShmProbe` (SHM regime diagnostic), `tools/SimCoach.GroundTruthDump` (extended),
  scratchpad `ld_probe.py` (spec-derived `.ld` parser) and the ghost decoder (in the RE log / workflow).
- KB: `acc-shared-memory-layout.md` (SHM-by-AC_STATUS + MoTeC `.ld` reality), `line-reference-and-gate-
  caveats.md` (self-data circularity), `claude-workflow-resource-limits.md` (host RAM / batching).
