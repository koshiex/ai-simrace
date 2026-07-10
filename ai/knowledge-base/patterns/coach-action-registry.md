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
