# Ground-truth revalidation run-book

The exit gate for the Phase-3 detection-truthfulness pack. It re-decodes a recorded ACC session,
computes an **independent** truth-oracle from the raw frames, re-runs the real compute pipeline over
the same frames, and asserts the emitted proto numbers match the oracle within coaching tolerance —
proving the two headline lies are gone: the **3929 ms "Curva Grande"** corner loss and the
**+14799 ms S1** sector loss.

The revalidation xUnit is **env-gated** and skips cleanly when no local fixture is present, so CI
stays green and the raw MCAP never enters the repo (privacy; `.gitignore *.mcap`). Because a skipped
run is still green, a PR that mutates the certified line/delta math must instead run the gate as a
**merge precondition** via `SIMCOACH_REQUIRE_GROUNDTRUTH` (see below). The committed pieces — dumper,
oracle, this doc, and the hermetic M3 defence-in-depth fact — are the permanent regression net.

## Pieces

| Piece | Path | Role |
| --- | --- | --- |
| Dumper | `tools/SimCoach.GroundTruthDump` | Decodes a session's `segment-*.mcap` via the real `McapSegmentEnumerator.Read` → per-frame CSV. |
| Oracle | `scripts/groundtruth_oracle.py` | pandas truth-oracle over the CSV, independent of pipeline code → `truth.json`. |
| Gate | `tests/SimCoach.Reference.Tests/GroundTruthRevalidationTests.cs` | Re-runs `ComputeSession` (Monza track model) and asserts against `truth.json`. |

## Prerequisites

- A recorded session directory (5 `segment-000[0-4].mcap` + `laps.parquet`). The reference fixture is
  `20260701-171602-738` (105201 frames, 11806 pit-lane frames, Monza / BMW M4 GT3 / dry-warm).
- Python 3 with `pandas` + `numpy`.
- The .NET SDK (WSL: `"/mnt/c/Program Files/dotnet/dotnet.exe"`). On a box with only a newer runtime,
  build/test roll forward; the dumper sets `RollForward=LatestMajor` for the same reason.

> **WSL note:** the dumper and the xUnit both run as Windows processes, so pass **Windows** paths
> (`C:\Users\...`), not `/mnt/c/...` paths. Env vars do not cross the WSL→Win32 boundary via a prefix —
> use `dotnet test -e KEY=VALUE`.

## Steps

1. **Dump raw frames to CSV.**

   ```bash
   "/mnt/c/Program Files/dotnet/dotnet.exe" run --project tools/SimCoach.GroundTruthDump -- \
     "C:\Users\<you>\AppData\Local\SimCoach\recordings\20260701-171602-738" \
     "C:\Users\<you>\...\frames.csv"
   ```

   Sanity: `WROTE 105201 frames`, `in_pit_lane frames: 11806`.

2. **Compute the truth-oracle.**

   ```bash
   python3 scripts/groundtruth_oracle.py frames.csv \
     "C:\Users\<you>\AppData\Local\SimCoach\recordings\20260701-171602-738\truth.json"
   ```

   The gate looks for `truth.json` next to the fixture (override with `SIMCOACH_GROUNDTRUTH_TRUTH`).
   Sanity: Curva Grande self time-at-position ≈ **3929 ms** (the value the span-mismatch bug leaked
   into `delta_ms`), Parabolica min speed ≈ **127.3 km/h**, flying S1 ≈ 35994 ms, out-lap S1 ≈ 66538 ms.

3. **Run the gate.**

   ```bash
   "/mnt/c/Program Files/dotnet/dotnet.exe" test tests/SimCoach.Reference.Tests \
     -e DOTNET_ROLL_FORWARD=LatestMajor \
     -e "SIMCOACH_GROUNDTRUTH_FIXTURE=C:\Users\<you>\AppData\Local\SimCoach\recordings\20260701-171602-738" \
     --filter GroundTruthRevalidation
   ```

   Without `SIMCOACH_GROUNDTRUTH_FIXTURE` the revalidation fact skips (returns green) and only the
   hermetic M3 fact runs — **unless** `SIMCOACH_REQUIRE_GROUNDTRUTH` is set (see below), in which case a
   missing fixture **fails** instead of skipping.

## Merge precondition (line/delta changes: M34-populate, M38-linedev)

The revalidation fact is env-gated and cannot run in CI (the MCAP + `truth.json` are off-repo), so a
green CI run **is not acceptance** — it only means the fact skipped. Any PR that mutates the
NO-GO-certified line/delta math — chiefly **`M34-populate`** and **`M38-linedev`**, which rewrite
`CornerEventBuilder`/`GridMetrics` — must therefore run the gate locally against the fixture and record
the green result in the PR body:

```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" test tests/SimCoach.Reference.Tests \
  -e DOTNET_ROLL_FORWARD=LatestMajor \
  -e "SIMCOACH_GROUNDTRUTH_FIXTURE=C:\Users\<you>\AppData\Local\SimCoach\recordings\20260701-171602-738" \
  -e SIMCOACH_REQUIRE_GROUNDTRUTH=1 \
  --filter GroundTruthRevalidation
```

`SIMCOACH_REQUIRE_GROUNDTRUTH=1` makes the fact **fail** rather than skip when the fixture is absent, so
a forgotten `SIMCOACH_GROUNDTRUTH_FIXTURE` can never masquerade as a green precondition. The fixture
(`truth.json` + the `20260701-171602-738` MCAP) is held **off-repo by the repo owner**; regenerate the
oracle side after any change to the dumper/oracle. Note that M43-gridindex shifts the oracle-vs-emitted
numbers sub-metre/sub-ms (the resampler-consistent index) — treat the post-M43 emitted values as the
new baseline when reading the tolerance bands below.

## What the gate asserts (and why the numbers look the way they do)

The fixture's on-disk reference was overwritten in place by this same PB lap, so the gate seeds a
reference from the flying lap and every reference-relative diff collapses to **~0 (self==ref)**. The
gate therefore asserts *both* the collapse *and* that the fixed number is far from the buggy value the
oracle measures independently (the discriminator that fails loudly if a fix regresses).

| Check | Acceptance | Result on the reference fixture |
| --- | --- | --- |
| Positive guard: non-empty `monza_t*` corners | #6 | 11 corner events (mis-wired empty harness fails loudly) |
| Curva Grande `delta_ms` ≠ ~3929, per-corner `delta_ms` ~0 and far from oracle span-time | #1 | t01..t11 all within ±13 ms of 0 (span times 2496–6077 ms) |
| Σ corner deltas ~0, not −1381 | #1 | −16 ms |
| `clean_lap_count == 1`; S1 `sector_avg_delta_ms[0]` ≠ ~+14799 | #2 | clean=1, S1 avg = +17 ms |
| Parabolica abs min speed ~127.3 km/h; `min_speed_diff_kmh` ~0 | #3 | oracle 127.3 km/h, emitted diff −0.0 km/h |
| Parabolica brake onset upstream of Start (M16), not StartPosition-fallback | #3 | onset 0.8595 < start 0.8828 |
| Render-path smoke: "3929" / "14799" never render | #4 | debrief top-loss = Lesmo 2, 27 ms |
| M3 guard drops sign-inverted/oversized corner AND sector loss | #5 | hermetic fact, always-on |

## Privacy

Raw telemetry (`*.mcap`), the intermediate CSV, and `truth.json` are **not** committed — they stay on
the dev machine next to the fixture. Only aggregate, oracle-derived numbers are ever surfaced.
