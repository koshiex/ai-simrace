# ADR-0021: Reference-kind taxonomy on the single `[references]` table

**Status**: Accepted
**Date**: 2026-07-15

## Context

Until now `[references]` held exactly one shape: the driver's PB lap for a `(track, car, weather)`
triple — a full resampled-lap parquet on disk (a per-metre grid carrying TIME via the boundary
`TimeAt` and LINE via world coordinates) plus a metadata row pointing at it.

M46 introduces a second, structurally different reference: the driver's **own-optimal** lap — the
theoretical best assembled from their best sector splits. It is not a lap that was ever driven, so it
has no continuous per-metre grid: it is *N per-sector best durations* and nothing else. A third kind,
an **alien** racing-line reference (LINE-only parquet), is on the PR-B3 roadmap.

We need these kinds to share the one `[references]` table (one active row per triple per kind, one read
path, one repository) without the `optimal` kind's file-less shape crashing the PB read path or the
`optimal` kind fabricating telemetry it does not have.

## Decision

One `[references]` table, a `kind` discriminator column, each kind read through a **non-overlapping
facet**:

- **`pb`** — full Parquet. TIME via boundary `TimeAt`, LINE via world coords. **Unchanged**; still the
  only kind the live delta/line hot path reads today.
- **`optimal`** — **row-only, NO Parquet**. The N per-sector best **durations** are stored as JSON on
  the row (`optimal_sector_ms`), with `sector_sources_json` recording which session/lap each best came
  from. It is read as *N numbers at the N sector boundaries* — never as a per-metre grid.
- **`alien_line`** — LINE Parquet (future PR-B3). Not built here.

The table keeps a Parquet **NOT NULL per-kind by CHECK**: `pb`/`alien_line` rows must have a
`parquet_path`; `optimal` rows must have `optimal_sector_ms` and may have a null `parquet_path`.
`kind` participates in the uniqueness key — `UNIQUE(track_id, car_id, weather_bucket, kind)` — so a
`pb` and an `optimal` reference coexist for the same triple.

`optimal` is the divergent kind and the reason for **migration 007** (a `UNIQUE`-key change plus a
now-nullable `parquet_path` cannot be `ALTER`ed, so 007 is a full table rebuild).

## Why row-only for `optimal`, not a minimal boundary-only Parquet

The invariant that matters is: **coaching never quotes a fabricated mid-sector time for the optimal
reference.** The optimal lap is a stitch of sectors from different laps; any per-metre time grid
between two sector boundaries would be *interpolated*, i.e. invented — a time that was never driven as
a continuous stretch.

Storing `optimal` as N durations read only at sector boundaries makes that invariant hold **by
construction**: there is no per-metre TIME grid, so there is nothing to interpolate and no
`GridMetrics.TimeAt` to accidentally call. A stitched grid + `TimeAt` would fabricate mid-sector times
the moment any consumer indexed it between boundaries.

The rejected alternative — a *minimal boundary-only Parquet* (a grid with samples only at the sector
boundaries) — reaches the same numbers but guards the invariant **only by convention**: nothing stops a
future caller from resampling or interpolating it into a dense grid and quoting an invented time. Row-
only removes the footgun rather than documenting it.

This was adversarially reviewed; verdict **KEEP-A-WITH-AMENDMENTS** (row-only stands; the amendments
are the per-kind CHECKs and the FK-integrity guard that landed in 007).

## Consequences

- Migration `007_reference_kind.sql` rebuilds `[references]`: adds `kind` (`NOT NULL DEFAULT 'pb'`),
  `optimal_sector_ms`, `sector_sources_json`; makes `parquet_path` nullable; changes the uniqueness key
  to include `kind`; adds `CHECK (kind='optimal' OR parquet_path IS NOT NULL)` and
  `CHECK (kind<>'optimal' OR optimal_sector_ms IS NOT NULL)`. Existing rows are stamped `kind='pb'`.
- The rebuild ends with an FK-integrity guard that aborts the migration if `pragma_foreign_key_check`
  reports any broken FK on `[references]` (nothing FK-references `references.id`, so it is normally
  empty; the guard is defensive). `RAISE()` is trigger-only in SQLite, so the abort is expressed as a
  temp table with a `CHECK` that fails when the violation count is non-zero.
- `ReferenceRow.ParquetPath` becomes nullable; `Kind`/`OptimalSectorMs`/`SectorSourcesJson` are added.
  `SimCoach.Storage` stays free of any dependency on `SimCoach.Reference`, so the row's `Kind` is the
  raw DB **string**; the `ReferenceKind` enum and its `pb`/`optimal` mapping live in `SimCoach.Reference`.
- `ReferenceRepository.GetByTriple` gains a `kind` parameter (default `"pb"`) so the PB read path is
  unchanged for existing callers. On the `pb`/`alien_line` read path a null `parquet_path` is a **hard
  error** (log + throw), so only `optimal` is legitimately file-less.
- `AlienLine` is deferred to PR-B3 — the enum ships with `Pb` and `Optimal` only.
