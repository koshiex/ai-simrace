# Reading a live coaching session from logs + DB (forensics)

How to assess what the coach actually did in a recorded/live session, and the gotchas that mislead.

## Where the data lives

Data root `%LOCALAPPDATA%/SimCoach` (WSL: `/mnt/c/Users/<you>/AppData/Local/SimCoach`):
- `recordings/<sessionId>/` — `segment-*.mcap` (raw frames) + `laps.parquet`. `sessionId` = `YYYYMMDD-HHMMSS-fff` in **UTC**.
- `logs/simcoach-YYYYMMDD.log` — Serilog text; one file per local day. Lines are timestamped in **local time** (e.g. `+03:00`).
- `simcoach.db` — SQLite: `sessions`, `laps`, `coach_tips`, `llm_usage`, `references`, `settings`. No `sqlite3` CLI on this box — use `python3` + `sqlite3` module.

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
