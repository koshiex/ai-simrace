# Knowledge Base Index

## docs/reference/

- [acc-shared-memory-layout.md](docs/reference/acc-shared-memory-layout.md) — ACC SHM layout provenance, golden sizes (800/1588/820), the `trackConfiguration[33]` bug in popular C# ports, pack-4 padding rules.
- [acc-poll-rate.md](docs/reference/acc-poll-rate.md) — why the emit rate collapsed to ~64 Hz (Windows 15.6 ms timer tick) and the `timeBeginPeriod(1)` / `WinTimerResolution` fix to hit 333 Hz.
- [acc-lap-boundary-timing.md](docs/reference/acc-lap-boundary-timing.md) — why the first live ACC session segmented to 0 laps: `completedLaps` increments ~1 frame before `normalizedCarPosition` wraps (pinned at 1.0), so the old lap-bump-AND-wrap predicate never fired; fix = wrap-primary (`prev>0.9 && cur<0.3`), `lap_number` dropped from the trigger (ADR-0012).
- [compute-domain-events.md](docs/reference/compute-domain-events.md) — PR-E: `ComputeService` lives in `Reference` (dep graph); `SessionManager` finalizes in `StopAsync` not the ExecuteAsync finally (else it races compute); `DomainEventFanOut` is lossless/unbounded; ref deltas from the grid; corner trackers reset on every crossing; shared `ResampledLapParquet` schema (ParquetSharp reads by indexed `Column(i)`); `ITrackLengthProvider` relocated to Storage.
- [coach-llm-host-wiring.md](docs/reference/coach-llm-host-wiring.md) — PR-H: `Llm:Live` is the single fake-vs-real switch *in the router* (Coach always calls the LLM); offline pair needs a rate (zero-cost rows, no key); `LlmOptions` is monitor-only for settings re-bind; settings config-source opened before `Build()`, below the env source; load-bearing Coach stop order (between recorder and compute); e2e drains via `ExecuteTask` not `RunAsync` (StopApplication aborts the drain); `session_id` seam over `SessionContext` (FK to `sessions`).

## tools/

- [dotnet-build-quirks.md](tools/dotnet-build-quirks.md) — testhost runtime roll-forward workaround, `.slnx` vs `.sln`, IDE0007/0008 var rules as build errors (apparent type requires `var`, non-apparent requires explicit), windows-latest CRLF/`.gitattributes` format failure, single-file publish needs Serilog `Using` list, `.gitignore` `data/` swallows vendored embedded resources (needs negation), NuGet packages that don't exist (MCAP, ParquetSharp 16.0.0).
