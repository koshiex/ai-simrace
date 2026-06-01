# Coding Conventions — SimCoach (C#/.NET 9)

Inherits user's global conventions (`~/.claude/rules/ecc/common/coding-style.md`); below are SimCoach-specific overlays.

---

## Project Setup

- Target framework: `net9.0`. Overlay project: `net9.0` (Avalonia, NOT `net9.0-windows`).
- `<Nullable>enable</Nullable>` everywhere.
- `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` everywhere.
- `<AnalysisLevel>latest</AnalysisLevel>` + `Microsoft.CodeAnalysis.NetAnalyzers` package.
- Default to `<ImplicitUsings>enable</ImplicitUsings>`.

## Immutability

- Prefer `record` over `class` for data types.
- `init`-only setters; never public `set`.
- `IReadOnlyList<T>` / `IReadOnlyDictionary<TK,TV>` on public surfaces.
- Mutation isolated to `internal sealed class` collectors (Pipeline buffers, etc.).

## Async

- All I/O is `async`. Never block (`Task.Result` / `.Wait()`).
- All `async` methods take `CancellationToken ct`. Tokens flow from `IHostedService.StopAsync`.
- Prefer `ValueTask<T>` for hot-path returns (telemetry frames).

## Channels (no MediatR)

- In-process pub/sub uses `System.Threading.Channels`.
- Bounded channels with `BoundedChannelFullMode.DropOldest` on telemetry hot path.
- Document channel capacity and back-pressure policy at the channel declaration site.

## Null Safety

- Never use null-forgiving (`!`) without an inline comment justifying it.
- Use `is not null` patterns; never `!= null`.
- For external boundaries (HTTP, file I/O, SHM), parse via `TryParse` patterns; never throw across the boundary.

## Error Handling

- Domain errors are `record`-based result types: `Result<T> = Ok(T) | Err(string)`.
- Reserve exceptions for programmer errors (`ArgumentNullException`, `InvalidOperationException`).
- `try-catch` only at top-of-loop boundaries (host services); log with `Serilog` and continue.
- Never swallow exceptions silently.

## Naming

- `PascalCase` for types, methods, public properties.
- `camelCase` for locals, parameters.
- `_camelCase` for private fields (with `readonly` where possible).
- Interfaces start with `I`: `ITtsBackend`, `ITelemetryAdapter`.
- Async methods end in `Async`.
- Hosted services end in `Service`: `IngestService`, `ComputeService`.

## Logging

- `Serilog` everywhere. Structured logging — never `string.Format`-into-message.
- Use enrichment: `LogContext.PushProperty("session_id", sessionId)`.
- Log levels:
  - `Trace` — per-frame telemetry (off in release)
  - `Debug` — per-event derivations
  - `Information` — session lifecycle, LLM calls
  - `Warning` — recoverable issues (LLM 429, schema retry)
  - `Error` — anything that breaks a session feature
  - `Fatal` — unrecoverable

## Configuration

- `Microsoft.Extensions.Configuration` with strongly-typed options bound via `IOptions<T>`.
- Validation via `OptionsBuilder<T>.ValidateDataAnnotations()` and `.Validate(...)`.
- All thresholds (e.g., `BrakeEarlyMeters = 2.0f`) are config-driven; no magic numbers in code.

## Files & Folders

- Many small files. One public type per file.
- Module folders mirror the namespace: `SimCoach.Coach/ActionRegistry/ActionRegistry.cs`.
- Tests mirror src: `tests/SimCoach.Coach.Tests/ActionRegistry/ActionRegistryTests.cs`.

## Comments

- Default: no comments. The code says what.
- Comments only for **why** that the code can't show: hidden invariants, workarounds, surprising business rules.
- Never reference issues, PRs, "added for X feature", or current-session context.

## Russian text

- All user-facing Russian text in `Resources.<Module>.resx` keyed by stable identifier.
- Coach phrase templates in `docs/02-architecture/action-registry.md` (and mirrored in the registry code).
- Code comments and identifiers in English.

## Forbidden

- DLL injection into other processes.
- `static` mutable state (logger excluded).
- Service-locator pattern (use constructor injection only).
- Reflection-based DI (Microsoft.Extensions.DependencyInjection is fine — registration is explicit).
- Newtonsoft.Json — use `System.Text.Json`.
- Auto-mapper-like libraries — write the mapping by hand.
