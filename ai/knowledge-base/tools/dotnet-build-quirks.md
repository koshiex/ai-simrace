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

## NuGet packages that do not exist (verified against nuget.org)

- **MCAP**: no C# package at all (`Mcap.Core` was a scaffold placeholder, removed).
  Minimal MCAP writer is hand-rolled in `SimCoach.Storage` per ADR-0003 risk mitigation.
- **ParquetSharp 16.0.0**: version skipped upstream; pinned to `16.1.0` in
  `Directory.Packages.props`. With `TreatWarningsAsErrors`, a missing exact version
  surfaces as NU1603 *error*, not a warning.
