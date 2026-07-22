# RU-eval gate calibration (M18 / pre-Phase-4 Track C1)

How the `SimCoach.RuEval` release gate got calibrated from real judge runs, what the numbers mean, and the
one production bug the calibration surfaced.

## What the gate is

`tests/SimCoach.RuEval/RuEvalGateTests` drives 6 committed fixtures (3 good + 3 known-bad anchors) through
the **production** Gold→prompt→LLM path, then has `anthropic/claude-sonnet-4.6` (route `ru_judge`, via
`OPENROUTER_API_KEY`) score each candidate 0–5 on five rubric dimensions (groundedness, brevity,
natural_russian, actionability, tone). Env-gated exactly like `GroundTruthRevalidationTests`: skips clean
unless **both** `SIMCOACH_RU_EVAL=1` and `OPENROUTER_API_KEY` are set, so offline CI stays green.

Three gate legs (`ScoreAggregator.Evaluate` → `EvalOutcome.Passed` = all three):
- **composite ≥ `PassBar`** (weighted fold, groundedness weighted heaviest at 0.35).
- **avg groundedness ≥ `GroundednessFloor`** — dedicated hard floor; fluent-but-ungrounded can never pass.
- **every dimension ≥ `MinDimensionScore`** — a single severe violation fails the fixture whatever the composite.

The three known-bad-anchor assertions are ALWAYS hard. The good-fixture bar is release-blocking only when
`EnforceGoodFixtureBar=true` — advisory before calibration.

## Calibration run (2026-07-22, 6 live runs)

Measured scores with the shipped values (`PassBar=3.5`, `GroundednessFloor=3.0`, `MinDimensionScore=2.0`):

| fixture | kind | composite (range) | discriminator |
| --- | --- | --- | --- |
| corner_nopb_understeer | good | 4.35 (stable) | all dims ≥ 3 |
| corner_ref_early_brake | good | 4.10–4.60 | all dims ≥ 3 |
| debrief_session | good | 4.50–5.00 **(post-fix)** | all dims ≥ 4 |
| knownbad_fabricated | anchor | 1.25–1.45 | **groundedness 0** every run |
| knownbad_raw_number | anchor | 2.55 (stable) | **tone 1** every run |
| knownbad_transliteration | anchor | 2.75–3.25 | **natural_russian 0** every run |

**Key insight: the per-dimension floor, not the composite bar, is the primary discriminator.** Each anchor is
engineered to tank exactly ONE dimension; its weighted composite can still creep toward the bar
(transliteration reached 3.25, ru=0), so `MinDimensionScore=2.0` is what deterministically rejects it. The
composite `PassBar=3.5` is the overall-quality bar for good fixtures (they clear it by ≥ 0.6). Both legs kept
at the pre-calibration defaults — the data confirmed them rather than moving them. `EnforceGoodFixtureBar`
flipped to `true` by default (`RuEvalOptions`).

## The bug calibration surfaced: debrief `top_priority` leaked raw «мс»

`debrief_session` was flaky before the fix (composite 2.70 / 3.20 / 4.35). Root cause: the RU-eval candidate
for the session cadence is the debrief `top_priority` field (`CandidateSource` → `TryValidateDebrief`), which
must be a clean spoken imperative (rule 5). The debrief prompt only scoped its ms-allowance to
`top_losses[].why` (rule 3), and the live LLM over-generalized it, stuffing "1840 мс" into `top_priority`. The
judge correctly tanked tone.

**Fixed at the source**, not by lowering the bar: `coach.system.debrief.v1.ru.txt` rule 5 now states
`top_priority` is number-free (мс/metres/km/h live only in `top_losses`). The deterministic `DebriefTemplate`
fallback and the fixture's reference phrase were already number-free — the prompt just didn't say so. Post-fix,
the fixture passes 4.50–5.00 stably. General lesson: **candidate-side flakiness (generation) is not judge-side
noise — `SampleCount` averages judge calls on ONE candidate and would not have fixed this.**

## Run-book

```bash
SIMCOACH_RU_EVAL=1 dotnet test tests/SimCoach.RuEval \
  -l "console;verbosity=detailed" --filter "FullyQualifiedName~RuEvalGateTests"
# needs OPENROUTER_API_KEY in env; ~40–50 s; ~$0.05–0.10 (6 Gemini generations + 6 Sonnet judge calls)
```

`-l "console;verbosity=detailed"` is required to see the per-fixture score dump (the `ITestOutputHelper`
lines: `g= b= ru= act= tone=` + composite + justification). Regen fixtures via `FixtureLoader` (edit a
`Fixtures/*.json` `event` block to the new proto shape after a Gold-schema change).

Not blocked by C2 (BalanceKernels fire-rate) — that one needs live ACC telemetry (owner's driving), which
macOS dev can't produce. See [[session-log-forensics]], [[compute-detection-truthfulness]].
