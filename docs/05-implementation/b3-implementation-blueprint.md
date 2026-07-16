# PR-B3 Implementation Blueprint — Ghost Alien-LINE (P3)

> **Judge verdict: APPROVE_WITH_CHANGES.** The architecture is sound and low-surprise: the taxonomy
> reuse genuinely needs no migration (migration-007's CHECKs admit `alien_line` — non-null
> `parquet_path` + null `optimal_sector_ms` satisfy both, and `UNIQUE(track,car,weather,kind)` lets
> it coexist with `pb`+`optimal`), the LINE-only `ResampledLap` mirrors `CenterlineLineReference.Build`,
> the tool/runtime split is respected, and the `ComputeSession.InitSession` seam is genuinely ~3 lines
> plus fault-isolation. **But the reviewed blueprint's central claim — that MUST-FIX #1 (the seam mask)
> is enforced end-to-end — is FALSE as drafted, and this is the CRITICAL blocker.** The 8 must-fixes
> below fold into the existing commit structure without rework. After they land, the sequence is safe
> to hand to an implementer.
>
> This document is the implementation-ready companion to
> [`beyond-pb-pr-plan.md`](./beyond-pb-pr-plan.md) (the authoritative reviewed plan — commits 18–22,
> the ghost architecture-fit section, MUST-FIX #1/#5, Owner decision points) and
> [`acc-ghost-format-re.md`](./acc-ghost-format-re.md) (the reverse-engineered `.ghost` format:
> container, payload, 130-byte records, log-clock, pedals, seam bins, validation guards). Orientation:
> [`beyond-pb-reference-status.md`](./beyond-pb-reference-status.md). Style/lockstep reference:
> [`b2-implementation-blueprint.md`](./b2-implementation-blueprint.md). It does not restate those; it
> folds every must-fix into the commit that owns it and records the fetch plan, the end-to-end
> seam-mask mechanism, the M38 re-tune, the `lap_dirty` fix, and the owner decisions.
>
> **Status: blueprint-only.** No owner greenlight to write code yet.

## Why the seam mask is the whole review

`GridMetrics.InterpWorldXZ/Tangent` interpolate every alien-grid bin blindly, and Parabolica
(pn 0.92–1.00) IS a real corner that raises a `CornerEvent`. The single-ghost line there is coin-flip
noise (std ~2.1 m, ~89 % sign-agree). MUST-FIX #1 exists to make that noise inert. **Verified against
source, the drafted runtime fix does not achieve that:**

- `CornerEventBuilder.cs:199` — `ev.RacingLineDeviationM = racingLineDeviationM;` is assigned
  **unconditionally**. `lineRelevant` (cs:190–191) gates only the SIGNED `Entry/Apex/Exit` fields
  (cs:200–202), not the unsigned RMS.
- `racing_line_deviation_m` is the **sole driver** of `tighten_apex` (actionRegistry.json:230,
  `op: gt, value: 0.5`).
- The reviewed blueprint's cited "existing short-circuit at `cs:165`" is actually the min-speed
  suppression ternary — **not** a line-deviation gate. The narrative anchor is wrong.

Therefore a corner whose `[Start,End]` band **straddles** a masked seam (real bins before pn 0.92 +
NaN bins after) computes a non-zero **partial** RMS over its unmasked frames and voices
"Ближе к апексу" — exactly the fabricated advice the mask must silence. Fixing this (M1) and proving
it falsifiably (M2) IS the runtime half of MUST-FIX #1; the signed-path `IsNaN`/`TurnSign` guards are
behavioral no-ops (`GridMetrics.InterpWorldTangent` already returns `(0,0)` for NaN-touching segments,
so `MedianSignedOffset` skips those frames before `refX` is read) and count only as cheap defensive
explicitness.

---

## Per-commit sequence (must-fixes folded)

Each commit ends green: `build + test + format` under `TreatWarningsAsErrors` (IDE0007/0008 `var`
rules and IDE1006 `_camelCase` private fields — **including `private static readonly`** — are build
errors; `dotnet format` (CI) surfaces IDE1006). The tool inherits strict analyzers via
`Directory.Build.props`, so `HttpClient`/`ZipArchive`/stream disposal must be clean or CA warnings fail
the build even in a throwaway tool. `scripts/bootstrap.sh` regenerates `SimCoach.sln` after adding the
tool + test projects. Build:
`"/mnt/c/Program Files/dotnet/dotnet.exe" build SimCoach.sln`; test a project:
`... test tests/<Proj> -e DOTNET_ROLL_FORWARD=Major`; format: `... format SimCoach.sln`.

**Recommended sequencing:** land **commit 23 (`lap_dirty`) FIRST** — it is fully independent of the
ghost work, and sequencing it first rebases the ghost commits onto a clean registry-count baseline.
Then 18 → 22 in order (the taxonomy foundation must precede the tool; the tool must precede the runtime
seam).

### Commit 23 — `fix(coach): drop spoken lap_dirty announcement`

Fully independent of ghost work; sequence first. `lap_dirty` lives **entirely** in embedded JSON with
zero C# references (the "resx retire" premise is a non-issue — the phrase is inline
`phrase_template_ru`, not resx). Drop is the only shippable option: `src/SimCoach.Overlay` has only a
csproj (no window/tip-sink/render surface) and M44 (overlay-instead-of-silence) is deferred to Phase 5,
so an overlay downgrade is not buildable today (owner decision **OD4**). `is_clean` stays (still used by
`lap_clean_focus is_clean==true`); `ran_wide` corner cadence is orthogonal and untouched.

- **Files:**
  - `src/SimCoach.Coach/Data/actionRegistry.json` — delete the `lap_dirty` block (505–518) and its
    adjacent comma; leave the rank-202 gap (the uniqueness test checks `OnlyHaveUniqueItems`, not
    contiguity — no renumbering).
  - `tests/SimCoach.Coach.Tests/ActionRegistryLoadTests.cs` — bump `HaveCount(34)` → `HaveCount(33)`
    (line 29). **Must land in this same commit** or the existing test goes RED.
  - `tests/SimCoach.Coach.Tests/ActionRegistryFilterTests.cs` — add a `CoachCadence.Lap`
    `DictionaryGoldView` with `is_clean=false`; assert `ValidSubset(...).Select(a => a.Id)` does NOT
    contain `"lap_dirty"`.
- **Tests:** registry loads with `HaveCount(33)`; priority uniqueness holds with the rank-202 gap
  (regression); a dirty-lap lap-cadence gold view yields no `lap_dirty` (and no other lap-cadence tip
  keyed off `is_clean==false`).
- **Verify green:** `... test tests/SimCoach.Coach.Tests -e DOTNET_ROLL_FORWARD=Major` (actionRegistry.json
  is an embedded resource — `dotnet test` picks up the count change).

### Commit 18 — `feat(reference): ReferenceKind.AlienLine + kind-suffixed reference filename + ADR-0021 addendum`

Sim-agnostic runtime taxonomy only. **No migration, no new DB column** — migration 007 already admits
`alien_line` (kind `<> 'optimal'` with non-null `parquet_path` satisfies both CHECKs;
`UNIQUE(track,car,weather,kind)` lets it coexist with `pb`+`optimal`). Just the enum value, its two
mapping arms, and a stable kind-encoding filename so an `alien_line` parquet cannot collide with a `pb`
parquet on the same triple.

- **Files:**
  - `src/SimCoach.Reference/ReferenceKind.cs` — add `AlienLine`; add
    `private const string AlienLineString = "alien_line"`; add the arm to **both** `ToDbString`
    (25–30) and `Parse` (33–42) — either alone leaves a throwing round-trip; update the class
    doc-comment (line 7) removing "intentionally absent".
  - `src/SimCoach.Reference/ReferenceTriple.cs` — add `ParquetFileName(ReferenceKind kind)` returning
    `<track>_<car>_<weather>_<kind>.parquet` (reuse the private `Sanitize`); leave the kind-less
    `ParquetFileName` property and `SnapshotFileName` untouched (`pb` keeps using `SnapshotFileName`;
    the kind-less prop stays test-only).
  - `docs/02-architecture/adr/0021-reference-kind-taxonomy.md` — addendum ratifying `alien_line` as
    LINE-only Parquet, ghost decode provisional (Monza/BMW verified, re-validate new car/track),
    single-ghost + seam-mask ship.
  - `tests/SimCoach.Reference.Tests/ReferenceKindTests.cs`,
    `tests/SimCoach.Reference.Tests/ReferenceTripleTests.cs`.
- **Folds:** filename-collision surface gap (`pb` uses `SnapshotFileName` so no production collision,
  but the helper prevents any future kind-less collision).
- **Tests:** `AlienLine` ↔ `"alien_line"` `ToDbString`/`Parse` round-trip; `Parse` throws
  `ArgumentException` on unknown (regression); `ToDbString` throws on undefined enum value;
  `ParquetFileName(AlienLine) != ParquetFileName(Pb)` and encodes `"alien_line"`; sanitization holds.
- **Verify green:** build + `tests/SimCoach.Reference.Tests` + format.

### Commit 19 — `feat(tools): SimCoach.GhostImport container/zlib decode + 130-byte record parse + import guards`

New offline ACC-specific tool `tools/SimCoach.GhostImport` mirroring `tools/SimCoach.Bake` csproj shape
(Exe, `RollForward=LatestMajor`, no `TargetFramework` — inherits `net9.0` + strict analyzers).
**ACC `.ghost` decode MUST live here, never in the sim-agnostic runtime.** Decode = UE4 chunked
container inflate (per-chunk `0x30` header, magic `c1 83 2a 9e`, concat inflate outputs) → payload
header parse (track-id string + record count) → 130-byte little-endian record parse extracting world
X(+0)/Y(+4)/Z(+8), yaw(+12), brake(+24)/throttle(+25) [decoded but LINE-only, not shipped as coaching]
→ fail-fast guards. Offsets per [`acc-ghost-format-re.md`](./acc-ghost-format-re.md).

- **Files:**
  - `tools/SimCoach.GhostImport/SimCoach.GhostImport.csproj` — mirror Bake; ProjectReferences
    `SimCoach.Reference` + `SimCoach.Storage` + `SimCoach.Contracts` (NOT `Adapters.ACC`);
    `HttpClient`+`ZipArchive` are BCL, no PackageReference; add
    `<InternalsVisibleTo Include="SimCoach.GhostImport.Tests"/>`.
  - `tools/SimCoach.GhostImport/Program.cs` — arg-driven console, int exit codes,
    `Console.WriteLine`/`Console.Error` (Bake/GroundTruthDump pattern).
  - `tools/SimCoach.GhostImport/GhostContainer.cs` — internal static: iterate `0x30`-byte chunk
    headers (validate u64 magic `0x9E2A83C1`), inflate each zlib stream at `chunk+0x30`, concatenate.
  - `tools/SimCoach.GhostImport/GhostPayload.cs` — internal static: parse header (+0 `payloadLen-4`,
    +4 `version==4`, +17 str-len, track-id string, then u32 record count), slice records.
  - `tools/SimCoach.GhostImport/GhostRecord.cs` — `readonly record struct GhostRecord(float WorldX,
    float WorldY, float WorldZ, float Yaw, float BrakeNorm, float ThrottleNorm, float RawTimestamp)`
    (one public type per file).
  - `tools/SimCoach.GhostImport/ImportGuards.cs` — internal static fail-fast: arithmetic
    `recStart + count*130 + 11 == payloadLen`; world-XZ inside track bbox; throw with a clear message.
  - `SimCoach.sln` — regenerated by `scripts/bootstrap.sh`.
  - `tests/SimCoach.GhostImport.Tests/SimCoach.GhostImport.Tests.csproj`,
    `tests/SimCoach.GhostImport.Tests/SyntheticGhostFixture.cs` (in-code byte stream to
    `acc-ghost-format-re.md` spec — **NEVER** a committed `.ghost`),
    `tests/SimCoach.GhostImport.Tests/GhostDecodeTests.cs`.
- **Folds:**
  - **#specimen** — decode unit test uses a SYNTHETIC in-code byte fixture, not a third-party/committed
    `.ghost` (owner-produced/anonymized only if ever a real specimen).
  - **M8 (LOW)** — document in the fixture-test header that it is a **regression/refactor guard**
    proving decoder-inverts-encoder self-consistency, NOT a format-correctness proof (a shared misread
    of `acc-ghost-format-re.md` offsets would green it). Promote OD5's manual real-`.ghost` validation
    to a written, required per-car/track checklist step so provisional-decode acceptance is gated by an
    auditable check, not an informal assumption. Import-time bbox + arithmetic guards remain the
    loud-failure backstop.
- **Tests:** synthetic multi-chunk container inflates + concatenates to expected payload length;
  payload parse yields expected record count + track-id string (e.g. `"monza"`); 130-byte parse extracts
  expected world X/Z + yaw for crafted records; arithmetic guard throws when `count*130+11 != payloadLen`;
  bbox guard throws when a record's world XZ is outside the track box.
- **Verify green:** build `SimCoach.sln` (incl new tool) + `tests/SimCoach.GhostImport.Tests` + format
  (watch strict-analyzer var/IDE1006 + undisposed-HttpClient/stream CA gates).

### Commit 20 — `feat(tools): loop-closure lap-split + centerline align + per-metre resample → LINE-only ResampledLap with seam validity mask`

Tool-side production of the alien LINE grid. No normalized-position channel exists in the `.ghost`, so
split laps by world-position loop closure. Nearest-point align the decoded world path onto the
embedded pb centerline for the triple (via `CenterlineGeometryDataset` — deterministic, present for
monza+spa; a runtime PB is not guaranteed at import time) with a ~2 m median-deviation ceiling guard
(fail-fast if exceeded). Per-metre resample to a `position_normalized` grid. Project into a LINE-only
`ResampledLap` EXACTLY like `CenterlineLineReference.Build` (populate `PositionNormalized`/`WorldX`/
`WorldZ` + `LapNumber` + `GridLength`; zero all 12 other channels). Compute the per-bin seam validity
mask and encode it into the emitted grid (see [Seam-mask plan](#seam-mask-plan-must-fix-1-end-to-end)).

> **M5 (MEDIUM) — FIRST THING IN THIS COMMIT, a GATING spike.** The entire seam carrier (D9) and
> commit-18's "no migration / no new column" premise rest on **NaN surviving ParquetSharp Write/Read**.
> Columns are non-nullable `Column<float>` written via raw `WriteBatch` (`ResampledLapParquet.cs:92–108`),
> so the value *should* round-trip — but ParquetSharp writes column min/max **statistics** by default,
> and NaN-in-stats is unverified in this codebase. Write a `ResampledLap` with NaN in `world_x`/`world_z`
> via `ReferenceParquetCodec.Write`, read back, assert `float.IsNaN` survives. **Proceed with the
> NaN-sentinel design ONLY if green.** Pre-register the fallback — a reserved **out-of-bbox non-NaN
> sentinel** honored by the SAME caller-side guard — so a coercion result is a known branch, not an
> unplanned `ResampledLapParquet` schema/migration change.

- **Files:**
  - `tools/SimCoach.GhostImport/LapSplitter.cs` — internal static: split by loop-closure
    (return-to-start-region).
  - `tools/SimCoach.GhostImport/CenterlineAligner.cs` — internal static: nearest-point align onto the
    target centerline world XZ; compute median deviation; fail-fast > `AlignmentDeviationCeilingM`.
  - `tools/SimCoach.GhostImport/LineResampler.cs` — internal static: per-metre resample; emit LINE-only
    `ResampledLap` mirroring `CenterlineLineReference.Build`.
  - `tools/SimCoach.GhostImport/SeamMask.cs` — internal static: mark bins in configured seam pn bands
    (default `[0.00,0.02]`, `[0.92,1.00]`); write masked bins' `WorldX`/`WorldZ` as the validity
    sentinel (NaN); `PositionNormalized` stays the true pn so the grid still spans 0..1.
  - `tools/SimCoach.GhostImport/GhostImportOptions.cs` — `record` of tool knobs: `SeamBands`,
    `AlignmentDeviationCeilingM`, `ResampleStepM` (all defaults, no magic numbers).
  - `tests/SimCoach.GhostImport.Tests/LapSplitAlignTests.cs`,
    `tests/SimCoach.GhostImport.Tests/SeamMaskEmitTests.cs`,
    `tests/SimCoach.GhostImport.Tests/ParquetNaNRoundTripTests.cs` (the M5 spike).
- **Folds:** MUST-FIX #1 data-shape half (explicit per-bin NaN validity mask for seam bins); OD9 (full
  suppression of pn 0.00–0.02 / 0.92–1.00); OD5 (~2 m median-deviation ceiling as fail-fast).
- **Tests:** loop-closure split yields expected lap count from a synthetic multi-lap path; alignment
  reports median deviation and FAILS when > ceiling; resample produces a monotonic 0..1 grid with all
  non-line channels zero; seam-mask emits NaN `WorldX`/`WorldZ` inside seam bands and real coords
  elsewhere; emitted `ResampledLap` is a single row group and round-trips NaN through
  `ReferenceParquetCodec.Write/Read` (NaN preserved, **proven, not assumed**).
- **Verify green:** build + `tests/SimCoach.GhostImport.Tests` + format.

### Commit 21 — `feat(tools): persist alien_line reference row + LINE Parquet with ghost provenance`

Follow the **runtime** persistence pattern (`ReferenceStore.cs:74–106`), NOT Bake's JSON-file output.
Resolve `DataRoot` identically to the App (`Storage:DataRoot` with `%VAR%` expansion, else
`%LOCALAPPDATA%/SimCoach`) so the tool writes where the App reads; parquet dir = `<DataRoot>/references`,
filename via the commit-18 kind-suffixed helper. Write the LINE-only `ResampledLap` via
`ReferenceParquetCodec.Write`, then `ReferenceRepository.Upsert` a `ReferenceRow{Kind=alien_line,
ParquetPath=non-null, LapTimeMs=ghost laptime, OptimalSectorMs=null, SourceSessionId=null,
SourceLapNumber=null, SectorSourcesJson=ghost provenance JSON}`. No ADR-0017 snapshot/prune (imported,
not a live PB). No migration.

- **Files:**
  - `tools/SimCoach.GhostImport/AlienReferenceWriter.cs` — internal static: DataRoot resolve +
    `ReferenceParquetCodec.Write` + build `ReferenceRow` + `ReferenceRepository.Upsert`.
  - `tools/SimCoach.GhostImport/GhostProvenance.cs` — `record` serialized (`System.Text.Json`, never
    Newtonsoft) into `SectorSourcesJson`: accreplay lapId, car, lapTimeMs, trackId, optional driver
    name (only when present — see **OD1**).
  - `tools/SimCoach.GhostImport/DataRootResolver.cs` — mirror `TelemetryComposition.ResolveDataRoot`
    (or take `--data-root` arg).
  - `tools/SimCoach.GhostImport/Program.cs` — wire fetch/decode/align/persist end to end with exit
    codes.
  - `src/SimCoach.Storage/Repositories/Rows.cs` — **doc-comment only**: note `alien_line` reuses
    `SectorSourcesJson` for import provenance (no struct/schema change).
  - `tests/SimCoach.GhostImport.Tests/AlienPersistTests.cs` — in-memory SQLite + temp references dir.
- **Folds:** provenance-has-no-home gap (plan `:207` directs ghost provenance into `sector_sources_json`;
  folded here with the `Rows.cs` doc-comment update — auditability without a new column).
- **Tests:** `Upsert` writes an `alien_line` row that `GetByTriple(..., "alien_line")` reads back and
  coexists with a `pb` row on the same triple (both resolve); persisted parquet round-trips to a
  LINE-only `ResampledLap` (position + world XZ populated, time/speed/pedals zero, seam bins NaN);
  `ReferenceRow` satisfies migration-007 CHECKs (non-null `parquet_path`, null `optimal_sector_ms`) —
  insert does not throw; `SectorSourcesJson` holds provenance JSON; `SourceSessionId`/`SourceLapNumber`
  are null.
- **Verify green:** build + `tests/SimCoach.GhostImport.Tests` + format.

### Commit 22 — `feat(reference): ComputeSession prefers alien_line for _lineReference + seam-mask suppression + M38 alien-regime gate/phrasing review`

The whole runtime integration. This commit carries the CRITICAL fix; treat it as the acceptance gate
for MUST-FIX #1.

**(1) Kind-parameterized read path (M4).** Implement `Get(ReferenceTriple triple,
ReferenceKind kind = Pb)` on the **existing single** `_lookup`, using `kind.ToDbString()`. **Strike**
the drafted "inject the alien lookup" (ComputeSession) and "DI-register the alien LINE lookup"
(TelemetryComposition) instructions — a redundant second same-type singleton would need keyed services
to disambiguate (D11 resolved to parameterize, not to add a sibling). Preserve the null-parquet
hard-throw and generalize its message/log from "PB reference row" to name the actual kind (it already
claims to cover `alien_line` at `ReferenceLookup.cs:36–37`).

**(2) Fault-isolated alien tier-1 (M3).** `InitSession` (cs:261–264): prefer an `alien_line`
`ResampledLap` for `_lineReference` ABOVE the centerline branch, but wrap it. `ReferenceLookup.Get`
hard-throws `InvalidOperationException` on a null `parquet_path`, and `ReferenceParquetCodec.Read`
throws `InvalidDataException` on a corrupt/multi-row-group file — and third-party imported data has
higher corruption risk than the owner PB. An uncaught throw exits `InitSession` → `Accept` on the FIRST
frame, poisoning every session for that triple. Add a `TryLoadAlienLine` helper wrapped in `try/catch`
for **BOTH `InvalidOperationException` AND `InvalidDataException`** (`LoadOptimalSectorTimes` at
cs:291–302 catches only the former — the alien path needs both), log a warning, and fall through to
centerline (`CenterlineLineReference.Build`) → null. **`_reference`/TIME is UNTOUCHED** so alien can
never leak into TIME.

**(3) Honor the seam mask — the runtime half of MUST-FIX #1 (M1 CRITICAL + M2 HIGH).**

- **M1 — corner-level gate on the UNSIGNED field.** `CornerEventBuilder.cs:199` assigns
  `ev.RacingLineDeviationM` unconditionally, and it is the sole driver of `tighten_apex`. Add:
  `bool cornerMasked = CornerBandMasked(lineRef, corner.StartPosition, corner.EndPosition);` (scan grid
  bins in the corner's pn band for any NaN), then `ev.RacingLineDeviationM = cornerMasked ? 0f :
  racingLineDeviationM;` and pass `0f` into the `CornerContribution`. `RacingLineDeviation` (the RMS
  loop) additionally skips NaN bins for a straddling band's unmasked portion. **This — not the signed
  guards — is the deliverable.** Also set `lineRelevant &&= !cornerMasked` so the signed fields stay
  gated.
- **Signed guards (cheap defensive explicitness only, NOT the deliverable):** add a 4th `continue` in
  `SignedLineDeviation.MedianSignedOffset` (58–86) when `float.IsNaN(refX)` (sibling of the `(0,0,0)`
  sentinel skip at 70); guard `TurnSign` (93–104) to return `0f` when a band-endpoint tangent sample is
  NaN. These are behavioral no-ops (`GridMetrics.InterpWorldTangent` already returns `(0,0)` for
  NaN-touching segments) — keep them for clarity, do not count them as the fix.
- **M6 — correct the narrative.** Fix the anchor `cs:165` → `cs:199`/`cs:200–202` in the plan and this
  blueprint, and state explicitly that `cs:199` (`racing_line_deviation_m`) is behind NEITHER
  short-circuit.

**(4) M38 alien-regime review (MUST-FIX #5 — registry/config edit, not new kernel code).** See the
[M38 re-tune plan](#m38-re-tune-plan-must-fix-5).

**(5) Weather-mismatch diagnostic (M7).** `_triple` is built from the live frame's `WeatherBucket`
(cs:247–248) and `GetByTriple` keys on it exactly, so an `alien_line` row stamped `dry-warm` silently
never resolves under `dry-cool`. When the alien tier returns null, query the repository for any
`alien_line` row on `(track,car)` ignoring weather; if one exists, log info that `alien_line` is present
under weather `{stored}` but the session is `{live}` and is therefore inactive. Does not change
resolution semantics.

- **Files:**
  - `src/SimCoach.Reference/ReferenceLookup.cs` — parameterize `Get(triple, kind = Pb)`; reuse/generalize
    the null-parquet-path hard-throw; return the decoded LINE grid for `alien_line`.
  - `src/SimCoach.Reference/ComputeSession.cs` — `TryLoadAlienLine` tier-1 (try/catch both exceptions) +
    weather diagnostic; log which line source won.
  - `src/SimCoach.Reference/CornerEventBuilder.cs` — `CornerBandMasked`; gate the unsigned
    `RacingLineDeviationM` (cs:199) and the `CornerContribution`; `lineRelevant &&= !cornerMasked`; skip
    NaN bins in the RMS loop.
  - `src/SimCoach.Reference/SignedLineDeviation.cs` — defensive `IsNaN(refX)` `continue` + `TurnSign`
    NaN guard.
  - `src/SimCoach.Reference/ComputeOptions.cs` — confirm `LineRelevanceMaxRadiusM` (78) stays
    IOptions/validated; document the alien-regime review; add a per-kind knob ONLY if the owner picks
    per-kind thresholds (**OD7**).
  - `src/SimCoach.Coach/Data/actionRegistry.json` — MUST-FIX #5: confirm/adjust the five line-deviation
    gates + directional RU phrasing for the alien regime (documented).
  - `tests/SimCoach.Reference.Tests/AlienLineInitSessionTests.cs`, `SeamSuppressionTests.cs`,
    `LineOnlyInvariantTests.cs`, `AlienPriorityTierTests.cs`, `AlienFaultIsolationTests.cs`;
    `tests/SimCoach.Coach.Tests/AlienRegimeGateTests.cs`.
  - **Struck:** any DI change in `src/SimCoach.App/TelemetryComposition.cs` (M4).
- **Folds:** MUST-FIX #1 runtime half (M1 + M2), M3, M4, M6, M7, MUST-FIX #5.
- **Tests:**
  - `InitSession` prefers `alien_line` over centerline over null (three-tier) — asserted by which
    `_lineReference` loads.
  - **M2 (falsifiable suppression):** an alien `ResampledLap` masked at `pn>=0.92`, a corner band
    ~`0.88..0.98` (real then NaN bins), self path 2–4 m off-line in the real portion with
    `min_speed_diff_kmh<0`; assert `racing_line_deviation_m == 0f` AND `ValidSubset` does NOT select
    `tighten_apex`. Goes RED without the M1 gate, GREEN only with it. Keep the unmasked strong-corner
    (Ascari band) positive assertion alongside — non-zero signed deviation + a live cue.
  - **Fault isolation (M3):** a null-parquet `alien_line` row and a corrupt-parquet `alien_line` row
    each fall through to centerline without exiting `InitSession`.
  - **Line-only invariant:** `alien_line` never feeds `_reference`/TIME; `TimeAt`/`SliceToFrames` never
    called on the alien grid.
  - **Alien-regime gate is config-honored:** changing the relevance-gate/threshold config flips whether
    a fast-corner alien difference is coachable (no magic number).
  - **Priority-tier:** alien line cues rank/select correctly without flooding.
  - **Weather diagnostic (M7):** a weather-mismatched `alien_line` row logs the present-but-inactive
    info line and resolution stays null → PB.
- **Verify green:** build `SimCoach.sln` + `tests/SimCoach.Reference.Tests` + `tests/SimCoach.Coach.Tests`
  + format.

---

## Fetch plan

**DEV-TIME FETCH — never in CI, never committed.** `tools/SimCoach.GhostImport` takes
`--fetch --track monza --car bmw_m4_gt3 [--lap-id 2273485]`.

1. **Leaderboard:** bare `GET https://www.accreplay.com/api/leaderboards/laps?trackId=3&group=GT3` →
   JSON `[{lapId,car,lapTime,...}]`; pick the same-car BMW M4 GT3 alien lap (Monza `lapId 2273485`,
   01:46.037, ~7 s under owner PB 113.000). Recommend same-car over the fastest Ferrari (**OD2**).
2. **Download:** `GET https://www.accreplay.com/api/laps/<lapId>/download-ghost` — a **bare** request
   returns **403**; **200 ONLY with browser headers** (`User-Agent` + `Referer:
   https://www.accreplay.com/...`), set via `HttpRequestMessage.Headers`. This operational detail is
   confirmed but omitted from `acc-ghost-format-re.md`; it circumvents an access control — see **OD1**.
   Body is a ZIP; extract inner `GhostCars/Offline/<track>/Dry_<Car>.ghost` with
   `System.IO.Compression.ZipArchive` (verify inner first 4 bytes == `c1 83 2a 9e`). The tool's
   string→accreplay-trackId map (`monza=3` confirmed; `spa` unknown) is defined **in the tool** (no such
   map exists elsewhere in the repo). `HttpClient`+`ZipArchive` are BCL — dispose them or CA warnings
   fail the build.

**COMMITTED vs FETCHED:**

- **COMMITTED** = tool source; the SYNTHETIC in-code byte-stream decode fixture (built to
  `acc-ghost-format-re.md`, NOT a real `.ghost`); the accreplay-id map; and (per **OD10**) NOTHING
  derived.
- **FETCHED / GENERATED dev-time only** = the real `.ghost` (transient, never written to git) and the
  derived `alien_line` row + LINE parquet (written to `<DataRoot>/references`, outside the repo, like a
  PB). Third-party `.ghost` committing is banned; the derived LINE parquet is a derivative of a
  third-party artifact and stays out of git too (**OD10**).

---

## Seam-mask plan (MUST-FIX #1, end-to-end)

Carrier = **NaN sentinel** in `world_x`/`world_z` (D9). Chosen over a new `bool[]` column: it needs NO
parquet-schema change and NO migration (honors "alien_line adds no column"), and it is the natural
sibling of the existing `(0,0,0)` torn-frame sentinel already skipped at `SignedLineDeviation.cs:70`.
**Contingent on M5's gating round-trip spike** — if ParquetSharp coerces NaN, fall back to the reserved
out-of-bbox non-NaN sentinel honored by the same caller-side guard.

1. **PRODUCE (commit 20, tool).** `SeamMask` marks bins whose pn lies in a configured seam band
   (default `[0.00,0.02]` Rettifilo/start-finish loop-closure artifact; `[0.92,1.00]` Parabolica seam +
   car-specific). Masked bins get `WorldX`/`WorldZ = float.NaN`; `PositionNormalized` keeps its true
   value so the grid still spans 0..1. Everything non-line stays zero. The strong REAL-difference
   corners (pn 0.45–0.59 Lesmo/Serraglio ~−2.1 m; pn 0.73–0.78 Ascari ~+3.1 m) are NOT masked and
   survive as coachable signal.
2. **PERSIST + ROUND-TRIP (commits 20/21).** `ReferenceParquetCodec.Write` emits one row group with NaN
   in the masked world cells; the M5 test asserts NaN survives `ReferenceParquetCodec.Read` (**proven by
   test, not assumed**).
3. **HONOR at the two LINE consumers (commit 22, runtime).** `GridMetrics.InterpWorldXZ/Tangent` stay
   seam-BLIND (they lerp NaN → NaN, conservatively extending a masked zone by up to one grid step —
   acceptable); the mask is enforced at the CALLERS.
   - **`CornerEventBuilder` (the load-bearing half).** `CornerBandMasked(lineRef, Start, End)` scans grid
     bins in the corner's pn band for any NaN. `ev.RacingLineDeviationM = cornerMasked ? 0f :
     racingLineDeviationM;` (cs:199, the unsigned RMS driving `tighten_apex` — the M1 CRITICAL fix);
     `RacingLineDeviation` skips NaN bins for a straddling band; `lineRelevant &&= !cornerMasked` gates
     the signed fields.
   - **`SignedLineDeviation.MedianSignedOffset`** — defensive `IsNaN(refX)` `continue` + `TurnSign` NaN
     guard (behavioral no-op, kept for explicitness).
4. **SUPPRESSION TEST (commit 22, M2 — falsifiable).** Build an alien `ResampledLap` masked at
   `pn>=0.92`; run a corner whose band **straddles** it (~`0.88..0.98`, real then NaN bins) with the
   self path 2–4 m off-line in the real portion; assert (i) `racing_line_deviation_m == 0f` and (ii)
   `ValidSubset` selects NO `tighten_apex` in that range. This goes RED without the M1 corner-level gate
   and GREEN only with it — a fully-masked-corner test would green even while the leak ships, so it is
   rejected as non-falsifiable. Then assert an UNMASKED strong corner (Ascari) still produces a non-zero
   signed deviation and a live cue.

---

## M38 re-tune plan (MUST-FIX #5)

Lands in commit 22 as a message-registry/config edit, **not** new kernel code. Two parts.

**(a) Relevance-gate review (config-driven).** `ComputeOptions.LineRelevanceMaxRadiusM` (default 300f,
validated `>0`) and the `CornerEventBuilder` LateralG-trigger neutralisation (cs:191) were tuned so
self-median line noise on fast/flat corners zeroes out. Against a real 2–4 m alien corridor those same
fast corners now show genuine offsets. **Deliverable:** KEEP the 300 m ceiling + LateralG suppression as
defaults (they correctly gate out kinks inside alignment noise ~1 m), and add a test asserting the gate
is honored on the alien path and that changing the config flips whether a fast-corner alien difference
becomes coachable (proves no magic number, satisfies the enforced "thresholds are config" rule). **Note
(OD7):** the LateralG neutralisation is a hardcoded string compare, NOT config, and it silently drops
signed deltas on exactly the fast corners where following the pro line saves most time — whether to
surface those is the owner's call; ship a test that LOCKS the current decision (fast/LateralG corners
intentionally NOT signed-line-coached; apex still handled by the now-gated unsigned cue).

**(b) RU phrasing.** The five line-deviation phrases (`tighten_entry`/`open_entry`/`tighten_apex`/
`tighten_exit`/`open_exit`) are DIRECTIONAL ("плотнее"/"шире"/"ближе к апексу"), not magnitude-aware.
**Recommend: CONFIRM** the existing phrasing reads correctly at 2–4 m — a directional "tighten/widen
toward the reference line" instruction is correct whether the reference is the driver's own median or a
pro corridor; the alien case is if anything MORE apt ("move toward the faster line"). No new `.resx` or
registry strings for phrasing. The ±0.5 m gates firing on more corners is DESIRED for an alien line and
is bounded against flooding by (i) FULL seam suppression removing the two artifact bins and (ii) the
existing priority/cadence selection capping tips per lap.

**Escalation (OD7 / D13).** IF the owner wants per-kind thresholds (larger gates only for alien, leaving
self-median untouched), the smallest config-driven change is a single
`ComputeOptions.AlienLineDeviationFloorM` honored by one predicate in `CornerEventBuilder` — the ONE
spot that would add a line of kernel logic. Ships only on owner request.

---

## MUST-FIX (folded)

Plan-level MUST-FIX #1 (CRITICAL) and #5 (HIGH) are the two B3 amendments from `beyond-pb-pr-plan.md`;
the 8 Judge findings below refine and complete them. All fold into the owning commit — no rework.

| # | Sev | Commit | Item |
|---|-----|--------|------|
| M1 | CRITICAL | 22 | Gate the **unsigned** `racing_line_deviation_m` (cs:199) at the corner level via `CornerBandMasked`, not just the signed fields (cs:200–202). It is the sole driver of `tighten_apex`; a straddling seam band leaks a partial-RMS "Ближе к апексу". This IS the runtime half of MUST-FIX #1; the drafted "cs:165 short-circuit" claim is factually false. |
| M2 | HIGH | 22 | Replace the fully-masked-corner suppression test (greens even while the leak ships) with a **straddling-band** case that goes RED without M1 and GREEN with it. Keep the Ascari positive assertion. |
| M3 | MEDIUM | 22 | Fault-isolate the throwing alien tier-1: `TryLoadAlienLine` in `try/catch` for BOTH `InvalidOperationException` AND `InvalidDataException` (cs:291–302 catches only the former), log + fall through to centerline → null. Keep `_reference`/TIME untouched. |
| M4 | MEDIUM | 22 | Resolve the DI contradiction: parameterize `Get(triple, kind = Pb)` on the single `_lookup`; STRIKE "inject the alien lookup" / "DI-register the alien LINE lookup" (a redundant same-type singleton needs keyed services). Generalize the null-parquet throw message to name the kind. |
| M5 | MEDIUM | 20 | Front-load the NaN-round-trip test as a **gating spike** before building the seam mechanism on it; pre-register the out-of-bbox non-NaN sentinel fallback so a coercion result is a known branch, not a schema/migration change. |
| M6 | MEDIUM | 22 | Correct the seam-suppression narrative: fix anchor `cs:165` → `cs:199`/`cs:200–202`; state `cs:199` is behind NEITHER short-circuit; reframe the runtime deliverable as M1+M2, with the signed guards as cheap defensive explicitness only. |
| M7 | MEDIUM | 22 | Add a weather-mismatch diagnostic so OD6's exact-match no-op is field-debuggable: when the alien tier returns null, log info if an `alien_line` row exists on `(track,car)` under a different weather bucket. No semantics change. |
| M8 | LOW | 19 | Make the decode's ground-truth story auditable: document the synthetic fixture as a self-consistency guard (NOT a format-correctness proof) and promote OD5's manual real-`.ghost` validation to a written per-car/track checklist. |

---

## Owner decision points

**RESOLVED (owner, 2026-07-17). Three overrides of the review recommendation — these are authoritative and supersede the per-OD recommendations below:**

- **OD1 → USE + COMMIT THE DERIVED ARTIFACT, NOT THE GHOST/NAME.** accreplay fetch is approved. **Commit the digested `alien_line` (the LINE parquet / row) — aggregate line coordinates, no attribution.** NEVER commit the raw `.ghost` (fetched dev-time, discarded). NEVER capture/store the driver name (drop it at parse, no opt-in flag). Tool README notes personal-use + owner-responsible-for-rights. (This resolves OD1 **and flips OD10 → vendored.**)
- **OD2 → FASTEST GT3 LAP PER TRACK (override of same-car).** Ship the fastest GT3 lap on the board per track (Monza = Ferrari 296, `lapId 3010828`, 01:40.030), NOT the same-car BMW. **Stamp the `alien_line` row under the OWNER's car triple** (`bmw_m4_gt3`,`dry-warm`) so it resolves at `InitSession`; `sector_sources_json` provenance records the source car + laptime (NO driver name). Car-difference caveat accepted by owner ("все комбинации трек×машина сейчас не покроем").
- **OD3 → ALL TRACKS WE CAN (override of Monza-only).** Tool is track-agnostic. Fetch/decode/validate the fastest lap for every track that has a **centerline alignment target** (required for the ~2 m guard + pn mapping) → today **Monza + Spa** (both have vendored M38 centerlines). Monza is proven; Spa needs accreplay-`trackId` discovery + per-track decode re-validation before its `alien_line` is vendored. Ship what passes the guards; document adding more once centerlines exist.
- **OD10 → VENDORED-COMMITTED (override of generate-local).** The derived `alien_line` parquet is committed as an embedded resource (mirror the M38 `centerline.{monza,spa}.json` embed + loader) so the feature works out-of-box. Only the DERIVED line ships — never the `.ghost`.

**Commit impact of the overrides:** commit 19 picks board pos 1 (fastest), not a car filter; commit 21 stamps under the owner triple + provenance-without-name AND adds the vendored embedded-resource + loader path (not data-root-only); commit 18/22 add the embedded `alien_line` loader (mirror centerline); track scope = Monza + Spa.

Defaults taken as recommended: OD4 DROP, OD5 accept+guards, OD6 `dry-warm`, OD7 confirm-phrasing+lock, OD8 single-ghost+mask, OD9 full suppression, OD11 pedals DEFER.

---

Original review recommendations (superseded where the RESOLVED block above differs). D9/D10/D11/D14 from the reviewed blueprint are implementation decisions already
resolved by the must-fixes (NaN carrier, parameterize-`Get`, dedicated test
project) — not owner decisions.

- [ ] **OD1 — Ghost-fetch legality.** The importer sends browser `User-Agent`+`Referer` specifically to
  turn accreplay's deliberate 403 into a 200, pulling third-party copyrighted ghost laps and optionally
  storing a third party's driver name. **Recommendation:** sign off explicitly on the accreplay
  ToS/personal-use basis BEFORE shipping the fetch; default driver-name capture to OMITTED (opt-in via a
  flag); keep the ban on committing `.ghost` AND any derivative (LINE parquet); document in the tool
  README that the owner is responsible for having rights to the imported artifact. **Rationale:** the
  mechanism circumvents an access control to pull third-party IP, which the repo's privacy posture
  ("only Gold-tier leaves the machine" governs outbound, not this inbound scraping) does not cover — it
  needs owner sign-off, not to be buried as an "operational detail".
- [ ] **OD2 — Which alien lap ships** — same-car BMW M4 GT3 (Monza `lapId 2273485`, 01:46.037) or the
  fastest Ferrari 296? **Recommendation:** same-car BMW M4 GT3. **Rationale:** signed 2–4 m line deltas
  are only actionable against a corridor the owner can physically replicate in the same car; a Ferrari's
  optimal line is partly non-actionable car-difference, and same-car also matches the owner's dry-warm
  BMW PB triple so `alien_line` resolves at `InitSession`.
- [ ] **OD3 — Which tracks in PR-B3** — Monza only or Monza + Spa? **Recommendation:** Monza only; Spa
  as a fast-follow. **Rationale:** Monza has confirmed decode, a vendored centerline alignment target,
  and known accreplay `trackId=3`; Spa's numeric id is unknown/not in the repo and decode needs
  per-track re-validation (OD5).
- [ ] **OD4 — `lap_dirty` (commit 23)** — drop the spoken announcement or downgrade to an overlay cue?
  **Recommendation:** DROP. **Rationale:** an overlay route is not buildable today —
  `src/SimCoach.Overlay` has only a csproj and M44 is deferred to Phase 5; drop is the only shippable
  option. `is_clean` (still used by `lap_clean_focus`) and `ran_wide` corner cadence are unaffected.
- [ ] **OD5 — Provisional decode** — ship on 2 files / 1 car / Monza only, or block on broader
  ghost-format validation? **Recommendation:** ACCEPT with the fail-fast guards (container magic +
  arithmetic + world-XZ bbox + loop-closure split + ~2 m alignment ceiling); REQUIRE re-validation
  against a real accreplay ghost before trusting any new car/track (M8's written checklist).
  **Rationale:** the guards make a bad decode fail loudly rather than silently mislead; the synthetic
  unit test proves only self-consistency, so a manual per-car/track ground-truth check is the real
  correctness gate.
- [ ] **OD6 — Alien weather bucket** — stamp the one bucket the owner races (`dry-warm`) or broaden the
  alien lookup to any `dry-*`? **Recommendation:** stamp `dry-warm` (matching the owner's Monza BMW PB
  filename) for ship; broaden to a `dry-*` fallback as a follow-up. **Rationale:** there is no single
  "dry" bucket in the vocab (dry-cool/dry-warm/damp/wet) and `InitSession` matches the exact bucket, so
  a mismatched stamp is a silent no-op; pin to the raced bucket now, paired with M7's diagnostic so a
  mis-stamp is debuggable.
- [ ] **OD7 — MUST-FIX #5 depth** — confirm existing directional RU phrasing + config relevance-gate
  review only, OR make fast/LateralG corners produce signed alien line cues (kind-aware LateralG skip
  and/or a per-kind `ComputeOptions.AlienLineDeviationFloorM`)? **Recommendation:** ship
  confirm-phrasing + a test that LOCKS the decision that fast/LateralG corners are intentionally NOT
  signed-line-coached (apex still handled by the now-gated unsigned cue); add the kind-aware knob ONLY on
  owner request. **Rationale:** directional phrasing reads correctly toward a pro line at 2–4 m and
  flooding is bounded by full seam suppression + priority selection; but the LateralG neutralisation
  (cs:191, a hardcoded string compare, NOT config) silently drops signed deltas on exactly the fast
  corners where following the pro line saves most time — surfacing those is the owner's call.
- [ ] **OD8 — Ghost source model** — single-ghost + seam mask now, or multi-ghost consensus line?
  **Recommendation:** single-ghost + seam mask (default); document consensus-median as a future upgrade.
  **Rationale:** simplest importer, and the seam validity mask (once enforced end-to-end via M1)
  suppresses the noisy Parabolica/start-finish bins that motivated consensus in the first place.
- [ ] **OD9 — Seam policy** — FULL suppression of pn 0.00–0.02 and 0.92–1.00, or partial?
  **Recommendation:** FULL suppression via the mask — but note this is only actually honored once M1
  gates the unsigned field; before M1 the Parabolica cue leaks. **Rationale:** those bins are
  loop-closure/car-specific artifacts (std ~2.1 m, ~89 % sign-agree at Parabolica); no Parabolica line
  coaching for now is the correct conservative choice, and it is the reason the CRITICAL must-fix must
  land.
- [ ] **OD10 — Derived `alien_line`** — vendored-committed or generated locally into
  `<DataRoot>/references` like a PB? **Recommendation:** generated-locally; commit NOTHING derived (each
  machine runs `GhostImport` once). **Rationale:** the LINE parquet is a derivative of a
  banned-to-commit third-party `.ghost` and sits on the same legal edge (ties to OD1); it also matches
  how pb/optimal references already live outside the repo.
- [ ] **OD11 — Ghost pedals/brake as a coaching input**, or LINE-only? **Recommendation:** DEFER —
  strictly LINE-only for PR-B3. **Rationale:** brake/throttle are decoded but single-car-verified; a
  line-anchored clock-free brake-point cue is a plausible future follow-up, not this set.

---

## Residual risks

- **NaN-in-Parquet is unverified until M5's spike runs.** If ParquetSharp's default column statistics
  coerce NaN, the entire seam carrier (D9) fails and the fallback touches `ResampledLapParquet` —
  invalidating commit-18's "no migration / no new column" premise. Front-loaded as a gating spike, but a
  live design dependency until proven.
- **The synthetic decode fixture proves only decoder↔encoder self-consistency.** A shared misread of
  `acc-ghost-format-re.md` field offsets (e.g. brake +24 vs +25, record stride) greens the test and ships
  a wrong world path. Bounded by import-time bbox/arithmetic guards + OD5's per-car/track manual
  validation, not by CI.
- **Provisional decode is verified on Monza/BMW only.** Any new car or track is unvalidated until
  re-checked; the tool's string→accreplay-trackId map (`monza=3` confirmed, `spa` unknown) is defined
  in-tool with no repo cross-check.
- **Whether Monza's specific baked Parabolica corner band actually straddles pn 0.92 depends on the
  baked track model** (not re-verified here). M1 is warranted regardless — nothing structurally prevents
  a corner band from overlapping a seam boundary — but the concrete in-game impact should be confirmed
  during OD5's manual validation.
- **Ghost-fetch ToS/copyright exposure (OD1) is a governance risk the owner must accept.** Not
  code-fixable; no must-fix removes it.
- **The alien line is advisory LINE-only and never feeds `_reference`/TIME** (verified: `InitSession`
  keeps `_reference = _lookup.Get(_triple)` untouched), so even a wrong alien corridor cannot corrupt
  delta/PB coaching — it degrades to bad line cues, bounded by the seam mask and priority selection.
