# Testing Strategy

**Coverage target**: 80% on `Pipeline.*`, `Coach.*`, `Reference.*`, `LLM.*`. 50% on `Overlay.*` / `Audio.*` (UI/audio harder to test deterministically).

---

## Test Tiers

### 1. Unit tests
- xUnit + FluentAssertions in each `tests/SimCoach.<Module>.Tests/` project.
- Pure functions only; no I/O, no time.
- Each test ≤ 50 ms; flaky tests are immediately quarantined.

### 2. Integration tests (replay-based)
- Recorded MCAP fixtures in `tests/fixtures/` (record real ACC sessions once, replay forever).
- Tests feed an MCAP fixture into Pipeline + Compute + Coach with a mocked LLM and assert:
  - Events emitted match `tests/fixtures/<scenario>.events.golden.json`
  - Gold artifacts match `tests/fixtures/<scenario>.gold.golden.json`
- Goldens updated only via `dotnet test -- --update-golden` (gated by env var).

### 3. End-to-end manual tests
Listed in `docs/05-implementation/e2e-test-plan.md` (added in Phase 7).

### 4. LLM eval (offline)
- A small Russian-coaching eval set in `tests/llm-eval/`.
- Runs against current default models; passes if ≥ 90% of phrases are:
  - Schema-valid
  - ≤ word-count limit
  - Use approved racing vocabulary

### 5. TTS eval
- For each Silero voice, generate 100 reference utterances, assert:
  - First-audio-frame ≤ 200 ms (measured via NAudio buffer fill time)
  - Cancel during synthesis removes audio within ≤ 50 ms
  - RMS energy stays in expected band (no silence bugs)

### 6. Performance smoke tests
- `Pipeline.IngestService` benchmark via BenchmarkDotNet — 333 Hz throughput on 4-core CPU.
- Overlay render benchmark — 30 Hz under 2 ms per frame on integrated GPU.

---

## Tooling

- `dotnet test --collect:"XPlat Code Coverage"` + `coverlet` + `reportgenerator`.
- `BenchmarkDotNet` for perf.
- `Moq` only for `HttpClient` (`HttpMessageHandler` mock) and `ITtsBackend`. Prefer hand-rolled fakes everywhere else (per global CLAUDE.md rule).

---

## CI Pipeline

```
on push / PR:
  - dotnet format --verify-no-changes
  - dotnet build --configuration Release
  - dotnet test --configuration Release --collect coverage
  - reportgenerator → publish coverage artifact
  - assert coverage ≥ 80% on protected modules (fail otherwise)
```

CI runs on Windows (native build target) and macOS (smoke-build of non-WPF/Avalonia-Windows projects).

---

## Anti-Cheat Smoke Test

Before any iRacing release:
- Launch iRacing while SimCoach is running.
- Confirm EAC produces no warning, no DLL load events into the iRacing process from SimCoach.
- Run with Process Monitor; assert SimCoach has zero handles into the iRacing process.

---

## Cost Smoke Test

Before any release that changes the LLM model defaults:
- Replay a 30-minute MCAP fixture into a non-mocked OpenRouter session.
- Assert end-to-end spend ≤ $0.10 (target: $0.05).
- Persist measured value to `tests/llm-eval/cost-runs.csv`.
