# Line reference (M38) semantics + ground-truth gate caveats

## The runtime median centerline is the driver's OWN median line, not an ideal

`MedianCenterlineBuilder.Build` aggregates the **median world position per metre across the driver's clean
laps** (the bake pools every clean lap per track from the local recordings). So the vendored
`centerline.<track>.json` — used as the M38 **LINE** reference for `racing_line_deviation_m` and the signed
per-phase deviations — is the *driver's own median corridor*, not an external/ideal racing line.

Consequence: a consistent driver deviates ~0 from their own median, so line-shape tips
(`tighten_entry`/`open_entry`/`tighten_exit`/`open_exit`/`tighten_apex`) stay quiet even with the centerline
active. The centerline is a **more stable** reference than a single noisy PB lap (median of many laps, so
one-lap artefacts don't pollute the LINE), and it catches a lap that wanders from the driver's usual line —
but it does **not** deliver "take a better line than you normally do" coaching. True ideal-line coaching
needs a different reference source (a fast/alien lap, a community line, or a geometrically-optimal line),
not the self-median. This is the residual half of the "slow-but-consistent driver goes quiet" problem.

The **TIME** reference (`delta_ms`, brake/throttle/min-speed diffs) is separate — it stays the driver's PB
(`ReferenceLookup`). Only the LINE reference became the centerline.

## A beyond-PB *absolute* speed target cannot be synthesised from the driver's own data — falsified twice

The "physics ideal line" idea (option A: build a friction-envelope QSS speed profile from the driver's
telemetry and coach "carry more speed here") was empirically falsified **twice** — see
`docs/05-implementation/ideal-line-reference-research.md` §6. First naively (µ inferred from one corner's
apex speed just reproduces that corner's ceiling). Then again after extending the ground-truth dumper with
**measured** `g_lat`/`g_long` + `world_x`/`world_z`, on the belief that measured grip would break the
circularity. It did not. The decisive signature: **`v_target > v_actual` at every corner identically for a
p95 / p98 / p99 grip envelope** — the ~26 km/h "budget" is envelope-*independent*, i.e. it is fixed
point-mass-QSS + median-line geometric optimism, not imported grip. The only genuinely non-circular term
(grip imported from *other* corners) was mean-negative and buried under the 3.6–8.9 km/h lap-to-lap noise
floor; and at half the corners the driver's own instantaneous apex g already met/exceeded the global
envelope, so "you're under-using grip" inverts into a crash tip. Takeaways for anyone revisiting this:

- Self-data → absolute apex-speed target is self-referential **by construction**. Don't retry it without an
  **external anchor**: track-boundary-constrained optimal line (vendor per car/track), or an external/replay
  reference lap (replay capture is now opt-in via `AccReaderOptions.AllowReplayCapture`).
- Measured `g_lat`/`g_long` carry curb/vertical transients that survive the `tyres_out` filter (raw abs-max
  ~3.1 g lat / ~7 g long persist); use a robust percentile **and** a sustained-window gate (≥0.3 s), not
  `max()` or `tyres_out` alone. p95 (~1.53 g) is more physical than p98 for a GT3.
- The safe, non-circular signal measured g *does* give is **relative** coaching (brake-release point,
  throttle-timing deltas vs PB), not an absolute apex-speed number.

## The corner-type gate silences fast corners by design

The M38 gate (`CornerEventBuilder`, `ComputeOptions.LineRelevanceMaxRadiusM` default 300 m) neutralises the
signed line deviations to 0 when `ApexRadiusM > ceiling` OR `Trigger == "LateralG"` (the
`CornerChannel.LateralG` flat/fast-sweep channel). On Monza that means Curva Grande, the Lesmos, Ascari and
Parabolica get no line-shape coaching — expected, not a bug. Only the tight chicanes are line-coachable.

## A passing ground-truth gate does NOT prove a feature is active ("green because dead")

The env-gated `GroundTruthRevalidationTests` asserts the certified TIME metrics (corner/sector `delta_ms`,
min-speed, brake onset) are unchanged. It reported "metric unchanged" after the M38 line-reference change
**precisely because the feature was inert** — the centerline asset was not embedded (missing csproj glob),
so `_lineReference` was always null and every corner fell back to the PB line, producing identical numbers.
A green gate confirmed nothing about M38. Confirm feature activation independently: the compute-start log
line now ends `…, reference true, centerline true` (grep the `%LOCALAPPDATA%/SimCoach/logs`), and an
embedded-`Load()` test guards the asset. Re-run the gate WITH the feature active to prove it still holds.

Running the gate is self-serviceable from WSL: the owner's live recordings sit at
`C:\Users\koba9\AppData\Local\SimCoach\recordings` (fixture `20260701-171602-738` + its `truth.json`), so
`dotnet test tests/SimCoach.Reference.Tests -e "SIMCOACH_GROUNDTRUTH_FIXTURE=C:\...\20260701-171602-738"
-e SIMCOACH_REQUIRE_GROUNDTRUTH=1 --filter GroundTruthRevalidation` runs the merge-precondition without
owner action. Read the DB at `%LOCALAPPDATA%/SimCoach/simcoach.db` with `python3 -c "import sqlite3…"`
(there is no `sqlite3` CLI in this WSL).

## `GridMetrics.Index` denominator must match the resampler

`PositionResampler` writes `PositionNormalized[k] = k / lapLengthM` with `gridLength = ceil(lapLengthM)`, so
the position→index inverse must divide by `lapLengthM`, **not** `(gridLength - 1)` (which is `< lapLengthM`
and drifts the index by up to one sample near the lap end). `GridMetrics.FracIndex` recovers the effective
length from the last stored sample (`(gridLength-1) / PositionNormalized[^1]`), giving an exact round-trip
`Index(PositionNormalized[k]) == k` on the grid the resampler produced. Synthetic test grids that set
`PositionNormalized[k] = k/(gridLength-1)` (last position exactly 1.0) mask the bug — they make old and new
identical; only a production-shaped grid (`lastPos < 1`) exercises the fix.
