# ADR-0017: Reference snapshots instead of in-place parquet overwrite

**Status**: Accepted
**Date**: 2026-07-05

## Context

A reference lap (the driver's PB for a `(track, car, weather)` triple) is stored two ways: a row in the
`[references]` table (metadata + the parquet path) and a resampled-lap parquet on disk. On every clean
lap that beats the stored time, `ReferenceStore.MaybeUpdate` **overwrites the single parquet in place**
(`references/<triple>.parquet`) and upserts the row.

That erases history. Once a faster PB lands, the previous reference — the exact channels every earlier
`delta_ms` / `racing_line_deviation_m` was measured against — is gone. A delta computed three sessions
ago can neither be reproduced nor audited, and there is no way to show progress of the reference line
over time.

P3 moves coaching toward an **absolute** reference (M34 signed line deviation, M38 median centerline).
As the reference model gets richer and more consequential, being unable to say *which* reference a past
number came from is a real gap. No P3 feature reads reference history yet, but the audit/reproducibility
property is cheap to preserve now and expensive to reconstruct later (the old parquet is already gone).

## Decision

Keep the **active pointer** unchanged and add an **append-only history**:

- `[references]` stays the single active row per triple (its `UNIQUE(track, car, weather)` and the live
  read path are untouched — zero risk to live coaching). Its `parquet_path` points at the newest
  snapshot file.
- A new `reference_snapshots` table records **every** PB parquet ever written for a triple: the triple,
  the source session/lap, the lap time, the (versioned) parquet path, and `created_at_utc`.
- Snapshots are written to **versioned filenames** (never overwritten), sortable by lap time and
  creation instant, so each historical reference survives on disk alongside its row.
- **Retention** is a config knob `MaxSnapshotsPerTriple`, **default keep-all** (unbounded). When set,
  the oldest snapshots beyond the cap are pruned (row + file).
- `reference_snapshots.source_session_id` carries an FK to `sessions(id)` **`ON DELETE SET NULL`** — a
  snapshot outlives the session that produced it (deleting a session must not delete its references).
  The parquet **file** is not FK-guarded: the session-delete cascade must never orphan or delete
  snapshot files; only explicit retention pruning removes them.

Full history is chosen over the cheaper hedge of "retain the raw parquet without a table": the owner
ratified the full table so snapshots carry queryable metadata (lap time, session, timestamp) for a
future progress/audit view, not just anonymous files.

## Why

- **Auditability & reproducibility.** Every past delta can be traced to the exact reference it used.
- **Live path unchanged.** Readers still resolve the active row's `parquet_path`; the history is
  write-side only, so the coaching hot path takes no new risk.
- **Cheap on a single-user desktop.** An append-only parquet per PB improvement is negligible for
  pre-alpha; the default keep-all is safe, and the cap exists for anyone who wants to bound disk.
- **Deletion stays safe.** `ON DELETE SET NULL` mirrors the existing `[references].source_session_id`
  behaviour (migration 001), so removing a session never removes its references.

## Tradeoffs

- **Disk grows unbounded by default.** Documented; `MaxSnapshotsPerTriple` bounds it when set. A PB that
  improves many times in one session writes many snapshots — acceptable for pre-alpha, revisit if it
  bites.
- **No consumer yet.** The history is written before any feature reads it (a deliberate, cheap hedge —
  the alternative is losing the data permanently). A progress/delta-over-time view is future work.
- **Two writes per PB** (snapshot file + row insert + active upsert) vs one overwrite. Negligible; both
  are local and off the coaching hot path.

## Consequences

- Migration `006_reference_snapshots.sql` adds the `reference_snapshots` table (contiguous version 6).
- `ReferenceStore.MaybeUpdate` writes a versioned snapshot, inserts a `reference_snapshots` row, and
  upserts the active `[references]` pointer to the new file — instead of overwriting one parquet.
- `ReferenceStorageOptions` gains `MaxSnapshotsPerTriple` (default keep-all); retention prunes oldest
  beyond the cap.
- `ReferenceTriple` gains a versioned snapshot-filename helper (same `Sanitize`, no path traversal).
