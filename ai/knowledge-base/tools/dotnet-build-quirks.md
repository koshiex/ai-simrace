# .NET build/test quirks in this repo

## Test host ignores csproj `RollForward` — use env var

On a machine with only a newer runtime (e.g. .NET 10) installed, `dotnet test` fails with
"You must install or update .NET" because the VSTest `testhost.dll` pins the 9.0 runtime.
Setting `<RollForward>LatestMajor</RollForward>` in the test csproj does NOT help — testhost
ships its own runtimeconfig.

Workaround:

```bash
DOTNET_ROLL_FORWARD=LatestMajor dotnet test SimCoach.sln
```

`global.json` already uses `rollForward: latestMajor` so build/restore work without the env var.

## SDK 10+ `dotnet new sln` creates `.slnx` by default

`scripts/bootstrap.sh` / `.ps1` pass `--format sln` (with fallback for older SDKs that
don't know the option). `SimCoach.sln` is committed, so the create path rarely runs.
Keep the classic `.sln`: `dotnet format` on SDK 9 is unreliable with `.slnx`.

## `.editorconfig` forbids `var` for non-apparent types — build error, not warning

`TreatWarningsAsErrors` + `csharp_style_var_elsewhere = false` means
`var builder = Host.CreateApplicationBuilder(...)` fails the build with IDE0008.
Use explicit types (`HostApplicationBuilder`, `IHost`) unless the type is apparent
(`var x = new Foo()` is fine).

## Naming rule IDE1006 covers `private static readonly` fields too

The repo's `.editorconfig` private-field rule (`_camelCase`) has no static carve-out, so
`private static readonly TimeSpan CollectTimeout` fails `dotnet format --verify-no-changes`
with IDE1006. Use `_collectTimeout`. Only `const` fields are exempt (separate PascalCase rule
via `required_modifiers = const`). Note: plain `dotnet build` does not surface IDE1006 —
only `dotnet format` does; CI runs both.

## HostApplicationBuilder: re-adding appsettings.json kills CLI/env overrides

`Host.CreateApplicationBuilder(args)` already loads appsettings.json + env vars + command-line
args in the correct precedence order. Appending another `AddJsonFile("appsettings.json")` puts
JSON *after* CLI args — command-line overrides silently stop working. To load config from the
executable directory, set `ContentRootPath = AppContext.BaseDirectory` via
`HostApplicationBuilderSettings` instead (see `src/SimCoach.App/Program.cs`).

## SDK 10 file-based apps are great for smoke scripts

`dotnet run script.cs` with a `#:project ../path/to/Project.csproj` directive compiles and runs
a single file referencing repo projects — no throwaway csproj needed. Used for generating
sample MCAP sessions when smoke-testing the app.

## NuGet packages that do not exist (verified against nuget.org)

- **MCAP**: no C# package at all (`Mcap.Core` was a scaffold placeholder, removed).
  Minimal MCAP writer is hand-rolled in `SimCoach.Storage` per ADR-0003 risk mitigation.
- **ParquetSharp 16.0.0**: version skipped upstream; pinned to `16.1.0` in
  `Directory.Packages.props`. With `TreatWarningsAsErrors`, a missing exact version
  surfaces as NU1603 *error*, not a warning.
