# ADR-0018: Phase-segmented signed line deviation — retain the unsigned RMS field

**Status**: Accepted
**Date**: 2026-07-05

## Context

`CornerEvent.racing_line_deviation_m` (field 9) is a single unsigned **RMS** distance between the self
world path and the reference line over the whole corner window. It powers the `tighten_apex` action
("Ближе к апексу") and is one of the numbers the NO-GO ground-truth gate certifies.

One scalar cannot tell the coach *where* or *which way* the line differs: a wide entry, a missed apex,
and an unused exit all fold into the same magnitude, and early-apex vs late-apex are indistinguishable.
M34 adds **phase-segmented signed** deviations (entry / apex / exit) so the coach can say "шире вход"
vs "уже выход". These are computed detection-side (a signed median perpendicular per phase band), not by
the corner LLM.

The open decision this ADR settles is **not** the sign convention (that lives inline in the proto field
comments, like fields 4/5/8/11/17 already do) — it is what to do with the existing field 9 once the
signed per-phase fields exist. "Renumber field 9" is **not** an option: the additive-only contract rule
(CLAUDE.md / AGENTS.md, ADR-0006) forbids renumbering or reusing a field number.

## Decision

**Add** three additive signed per-phase fields to `CornerEvent`:

| Field | № | Meaning |
|---|---|---|
| `entry_line_deviation_m` | 18 | signed; `+` = self runs **wider** than the reference line, `−` = **tighter** |
| `apex_line_deviation_m` | 19 | signed, same convention |
| `exit_line_deviation_m` | 20 | signed, same convention (carries the former `track_width` intent as exit line shape) |

and **retain** `racing_line_deviation_m` (field 9), unsigned RMS, populated exactly as today.

- The sign folds with corner turn direction so `+` always means "wider / outside the line" regardless of
  a left- vs right-hander. On a corner whose direction is ambiguous (a flat/kink), the signed fields are
  neutralised (0) — the compute-side gate M38 owns that call.
- The RMS field is **kept, not deprecated in place**: it is not derivable from the three per-phase
  signed values (an RMS over the full window ≠ any function of three band medians), it still backs the
  shipped `tighten_apex` action and the ground-truth gate, and it is the phase-agnostic magnitude used
  when a phase band is empty.

The signed fields drive **line-shape** coaching only (wider/tighter per phase). Off-track / over-limits
coaching stays owned by the existing `ran_wide` action (`off_track == true` from `tyres_out`) — the
line-deviation sign is deviation from a **line reference**, not from the physical track edge, so it must
never emit "over limits" / "use full track width" wording. (See M34/M38 in the P3 plan.)

## Why

- **Locates and directs the error.** Entry/apex/exit signs turn one opaque magnitude into an actionable
  "where + which way".
- **Additive and non-breaking.** New field numbers only; field 9 and every existing consumer keep
  working, so the ground-truth gate stays meaningful across the change.
- **Truthful.** The sign is deviation vs the reference line; conflating it with track limits would double
  up (wrongly) with `ran_wide`. Keeping the two separate preserves detection truthfulness — the whole
  point of the P0 pack this builds on.

## Consequences

- `telemetry.proto` `CornerEvent` gains fields 18/19/20 (M34-proto); the sign convention is documented
  inline on those fields.
- A pure `SignedLineDeviation` kernel computes the signed median perpendicular over caller-supplied
  `[lo, hi]` phase bands (M34-kernel), reusing the M43 `FracIndex` / a new `InterpWorldTangent`.
- `CornerEventBuilder` populates the three fields on the reference branch only; field 9 is unchanged
  (M34-populate) — this commit changes shipping line math, so it carries the M43-gate ground-truth
  precondition.
- The Gold layer surfaces the three fields and the action registry gains per-phase line-shape actions
  (M34-coach); a single exit action reads the sign of field 20 (`+` → tighten exit, `−` → open exit).
