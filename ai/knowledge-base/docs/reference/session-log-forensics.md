# Reading a live coaching session from logs + DB (forensics)

How to assess what the coach actually did in a recorded/live session, and the gotchas that mislead.

## Where the data lives

Data root `%LOCALAPPDATA%/SimCoach` (WSL: `/mnt/c/Users/<you>/AppData/Local/SimCoach`):
- `recordings/<sessionId>/` — `segment-*.mcap` (raw frames) + `laps.parquet`. `sessionId` = `YYYYMMDD-HHMMSS-fff` in **UTC**.
- `logs/simcoach-YYYYMMDD.log` — Serilog text; one file per local day. Lines are timestamped in **local time** (e.g. `+03:00`).
- `simcoach.db` — SQLite: `sessions`, `laps`, `coach_tips`, `llm_usage`, `references`, `reference_snapshots`, `settings`. No `sqlite3` CLI on this box — use `python3` + `sqlite3` module. `laps` columns are `lap_number,lap_time_ms,delta_vs_reference_ms,is_pb,is_clean,s1_ms,s2_ms,s3_ms,raw_offset_in_mcap` — there is **no `is_valid`** (only `is_clean`); a query assuming it errors `no such column`. **`references` is a SQL reserved word — quote it (`FROM "references"`) or the query is a syntax error.** `sessions` PK is **`id`** (the `YYYYMMDD-...` session id), not `session_id`; `laps.session_id` / `references.source_session_id` FK to it.

## Gotcha 1 — `coach_tips.lap_number` is off-by-one; use the LOG timeline for true per-lap cadence

Corner tips are stamped with the *previous/just-completed* lap number, not the lap they were spoken on. So `SELECT lap_number, COUNT(*) FROM coach_tips GROUP BY lap_number` **misattributes** corner tips by one lap (e.g. an invalid lap that was actually silenced shows a full count; the clean lap that got them shows fewer). To reconstruct real per-lap cadence, read the **log**: `Coach tip [...]` lines are timestamped in emission order, and the `Lap`-cadence tip (`lap_pb`/`lap_dirty`) + `Reference updated` lines mark each lap boundary. Count corner tips *between* those markers.

## Gotcha 2 — session id is UTC, log timestamps are local

`sessionId` (and `sessions.started_at_utc`) are UTC; log lines are local (`+03:00` here). To grep a session's log window, convert: a session at `12:37Z` is `15:37` local. Grepping the log by the raw session-id time misses everything.

## What to check per Wave (quick map)

- **Cadence / "wall of tips"**: count `["Corner"` lines per lap in the log. Note severity — `["Corner"/"High"]` tips bypass the M10 governor (floor / per-cadence cooldown / global cooldown / per-lap cap) and, once shipped, cross-lap dedup. If almost every tip is `High`, the cap/dedup look toothless — check `CoachOptions.SeverityBands` (severity was corner-*phase*-based: Entry/Brake→High; M45 moved it to time-loss magnitude).
- **Repeats**: grep identical `action_id` + corner across laps (e.g. `smoother_steering ... Гранде` twice). Cross-lap dedup (M32) only bites once High-severity tips are also deduped (M32-high) and severity is magnitude-based (M45).
- **Cold-start (M19)**: on the first lap (no reference) look for `ran_wide` / `trail_brake_absent`; absence there used to be silence.
- **Fallback (M22)**: `fellBack=true rejection=Timeout` on a `source="Template"` tip = the LLM failed and the template backstop fired.
- **Debrief metrics (M20)**: consistency-stddev needs ≥2 clean laps or it null-drops — a 1-clean-lap session legitimately shows no metrics line.
- **`N position reset(s) ignored`** in the log = off-tracks/teleports/pit dives, not lap crossings — a high count means a messy lap (explains a burst of `ran_wide`).

## Gotcha 3 — the Gold payload (the LLM input) is never persisted or logged

`coach_tips` stores only the *rendered* LLM output (`phrase_ru`, `top_losses_json`, etc.), never the Gold session/corner JSON that fed the prompt. Nothing writes the Gold artifact to disk and nothing logs it (no `WriteAllText`/`File.` in `SimCoach.Coach/Gold`, no gold/payload debug line in `SimCoach.LLM`). So after a drive you **cannot directly confirm a session-level Gold metric** (`optimal_gap_ms`, `theoretical_best_gap_ms`, a dominant-channel value, a trend) actually reached the model — only *infer* it from the compute inputs (DB rows) and the fact the debrief emitted without error. Consequence for acceptance tests: asserting a non-zero diagnostic (e.g. `dominant_channel`/trend) has to run through a **replay + test harness** that reads the domain-event/Gold object in-process, not by grepping a log or a DB column.

## M46 optimal ("theoretical best") reference — how to confirm it fired

- The optimal reference is a **row-only** record in `references`: `kind='optimal'`, `parquet_path IS NULL`, `optimal_sector_ms` a JSON array of per-sector best **durations** (e.g. `[35619,39607,36730]`), `sector_sources_json` the provenance (which session+lap each sector best came from), `lap_time_ms` = Σ those durations. A `pb` and an `optimal` row coexist per triple after migration 007 (`user_version=7`).
- **Stored durations, read cumulative:** `OptimalReferenceLookup.GetSectorTimes` prefix-sums the durations to cumulative boundaries; `ComputeSession.OptimalLapDelta` uses `cumulative[^1]` (= Σ = the target). So `[35619,39607,36730]` → target `111956`. Do not read the stored array as cumulative — it is not monotonic.
- **Bake confirmation** is in the log at app start (the baker is a one-shot `StartAsync` catch-up, off the hot path): `Optimal baked for <track>/<car>/<weather>: target <N> ms vs PB <M> ms` and `Optimal catch-up bake complete: X of Y PB triples produced an optimal`. `Compute started ... optimal true` confirms the session loaded it.
- **`X of Y` < all is normal:** a PB triple yields no optimal when it lacks enough clean-lap sector coverage or the gain is below `MinOptimalGainMs` — a triple with a single early session legitimately produces no optimal row.
- **The debrief may not verbalize the optimal gap** even when it fired: the gap is one metric in the Gold payload; the LLM can (correctly) lead the debrief from the largest actionable corner loss instead. Absence of the words "теоретический предел"/gap in `phrase_ru` is not evidence the metric was missing (see Gotcha 3 — you can't see the Gold input).

## Which LINE reference activated — read it off the compute-start log

- `Compute started ... line {source}` names the active LINE tier for the session: **`alien_line`** (PR-B3 imported/vendored beyond-PB corridor), **`centerline`** (M38 baked median), or **`pb-fallback`** (the PB world path — no line vendored). This is the fastest way to confirm in-game that the alien line resolved (e.g. `line alien_line` for a dry Monza/Spa session). The alien line is the **embedded vendored** asset — it writes NO `references` row, so a "no alien_line row in the DB" is expected and not a failure. The dry-weather gate (OD12) means a `damp`/`wet` session logs `line centerline`/`pb-fallback` even where an alien line is vendored.

## Gotcha 4 — `is_pb` = session-best AND beat the stored PB (two decoupled notions)

`ComputeSession.HandleLap` tracks two different "best" notions that were once conflated:
- **`isSessionBest`** = `clean && LapTimeMs < _runningBestMs` (running best of THIS session, seeded fresh). Drives `_runningBestMs`, `sessions.pb_time_ms`, and the M3 pre-overwrite deficit budget (`_bestLapDeficitMs`).
- **`IsPb`** (`LapEvent`/`laps.is_pb` + the `lap_pb` "Личный рекорд" tip + Gold) = `isSessionBest && deltaMs is null or < 0` — a session best that ALSO beats the stored PB reference (`deltaMs < 0`), or the first record when no PB exists yet (`deltaMs` null). A session best that is still **slower** than the stored PB (`delta_vs_reference_ms > 0`) is NOT `is_pb` and does **not** announce a record.

So `is_pb=1` now means a genuine PB. The all-time PB is the `references` row with `kind='pb'`; `laps.delta_vs_reference_ms` is measured against it (negative = beat it). Historically (pre-fix) `is_pb` was session-best only, so a slower-than-PB lap could falsely claim "Личный рекорд" — if you see that in an OLD session's data, it predates this fix.
