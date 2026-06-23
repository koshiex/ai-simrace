# Knowledge Base Index

## docs/reference/

- [acc-shared-memory-layout.md](docs/reference/acc-shared-memory-layout.md) — ACC SHM layout provenance, golden sizes (800/1588/820), the `trackConfiguration[33]` bug in popular C# ports, pack-4 padding rules.
- [acc-poll-rate.md](docs/reference/acc-poll-rate.md) — why the emit rate collapsed to ~64 Hz (Windows 15.6 ms timer tick) and the `timeBeginPeriod(1)` / `WinTimerResolution` fix to hit 333 Hz.
- [landmark-dataset.md](docs/reference/landmark-dataset.md) — vendored CrewChief corner-landmark JSON: use ACC `accTrackName` (not `acTrackNames`), 8 ACC tracks covered, metres→normalized + range-check drops bad tracks to derive.

## tools/

- [dotnet-build-quirks.md](tools/dotnet-build-quirks.md) — testhost runtime roll-forward workaround, `.slnx` vs `.sln`, IDE0007/0008 var rules as build errors (apparent type requires `var`, non-apparent requires explicit), windows-latest CRLF/`.gitattributes` format failure, single-file publish needs Serilog `Using` list, `.gitignore` `data/` swallows vendored embedded resources (needs negation), NuGet packages that don't exist (MCAP, ParquetSharp 16.0.0).
