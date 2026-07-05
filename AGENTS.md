# Agent Instructions — SimCoach Repo

For AI coding agents working in this repository (Claude Code, Cursor, etc.).

---

## Project at a Glance

- **What**: AI sim racing coach for Windows. Russian-language voice + minimalist overlay.
- **MVP target**: ACC. Roadmap: iRacing, LMU, F1 25.
- **Stack**: C#/.NET 9, Avalonia overlay, OpenRouter LLM, Silero v5 RU TTS via ONNX Runtime.
- **Status**: pre-alpha, scaffold + docs only.

Read first: `docs/01-product/PRD.md`, `docs/02-architecture/architecture.md`, `docs/03-functional/functional-requirements.md`, all ADRs in `docs/02-architecture/adr/`.

---

## Hard Rules

1. **No DLL injection, ever.** (ADR-0007) Overlay is a separate transparent topmost window. Telemetry via official shared memory only.
2. **Only Gold-tier JSON leaves the machine.** Raw telemetry stays on disk. (Privacy doc.)
3. **The LLM picks `action_id`s from `docs/02-architecture/action-registry.md`.** No free-form prose for in-corner / sector / lap cadences. Post-session is the only free-form cadence (constrained to ≤ 200 words).
4. **Russian phrase length is enforced:** ≤ 8 words in-corner, ≤ 25 words sector/lap, ≤ 200 words debrief. Reject violators.
5. **Default models are cheap (Gemini 2.5 Flash + DeepSeek V3.2).** Premium tier is opt-in.
6. **Avalonia, not WPF.** (ADR-0002) For Mac-dev compatibility.
7. **Many small files. Records over classes. Channels over events.** Per `docs/06-style/coding-conventions.md`.

## Forbidden

- Newtonsoft.Json (use `System.Text.Json`)
- Reflection-heavy DI
- MediatR / event aggregators (use `System.Threading.Channels`)
- Mutable static state
- `dynamic` (except at FFI boundaries)
- Sleep-based polling in tests (use `await`)

## When to Ask the User

- Before adding a new top-level dependency (cost, license, native bindings).
- Before introducing a new external service / API.
- Before any code that touches the game process beyond shared-memory reads.
- Before any change that affects the FR list — must update `functional-requirements.md` first.

## When NOT to Ask

- Adjusting tests, fixtures, internal refactors that don't change FRs.
- Doc edits.
- Adding an ADR for a non-controversial decision.

## File Naming

- Russian text → `Resources.<Module>.ru.resx`. Identifiers in code stay English.
- ADRs: `docs/02-architecture/adr/NNNN-kebab-case-title.md`. Increment NNNN.
- Tests mirror src: `tests/SimCoach.<Module>.Tests/.../<Type>Tests.cs`.

## Commit Style

- Conventional commits: `feat: ...`, `fix: ...`, `docs: ...`, `refactor: ...`, `test: ...`, `chore: ...`.
- One logical change per commit.
- Reference FR-### IDs in commit messages where applicable.
- Do not add Co-Authored-By trailers; user disabled attribution globally.
- Every PR description must list test cases: for each, the **expected result**, **where to look**
  (test name/file, log line, or UI element), and **how to reproduce** — running the unit tests, and
  on a Windows device (live ACC in-game, or the built `SimCoach.App.exe` / a replay session) when the
  change is runtime-observable. If the change is **not** runtime-observable (e.g. a contract-only /
  dead-until-wired PR), say so explicitly and give the build + unit-test reproduction instead.
- Write PR descriptions in an **impersonal, change-focused** register — headings like *"Что было
  исправлено" / "Что изменилось" / "Зачем"*, in passive/neutral phrasing ("исправлено", "добавлено",
  "теперь …"), like release notes. **Not** first person ("я/мы добавил"), **not** agent narration
  ("the agent did …", "this workflow …").
- **Never** put a Claude Code session link (`https://claude.ai/code/session_...`) — or any other
  agent/session reference — in a PR description.
- **Fix all PR/diff-review findings in the same session — must-fix, low, AND nits.** Do not defer
  ("worth a follow-up", "leave as-is") unless the owner explicitly says so; fix them while the context
  is loaded.
- **Reserve heavy multi-agent Workflow / ultracode orchestration for a PR/pack with many tasks**,
  where parallelism + adversarial Strict→Defence→Judge genuinely raise quality. Don't spin up a
  Workflow/ultracode for a small/simple change. A **single agent / sub-agent is fine even for small
  tasks** — only Workflows/ultracode are restricted, not sub-agents.

## Run-Throughs

- Format: `dotnet format`
- Build: `dotnet build`
- Test: `dotnet test`
- Coverage: `dotnet test --collect:"XPlat Code Coverage"`

(On macOS, only non-Avalonia-Windows-specific projects build. Run the full build on Windows or CI.)
