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

**A standalone tool/app exe is the opposite case — it DOES honour csproj `RollForward`.** The testhost
ignores it (it ships its own runtimeconfig), but a plain `dotnet run`/`.exe` reads its own generated
`*.runtimeconfig.json`, so `<RollForward>LatestMajor</RollForward>` in the tool's csproj lets it run on a
newer-only box. `tools/SimCoach.Bake` and `tools/SimCoach.GroundTruthDump` set it for this reason — without
it, `SimCoach.Bake.exe` on a .NET-10-only machine dies with "You must install .NET 9.0.0". The env-var
prefix does NOT rescue a `dotnet run` (it doesn't cross WSL→Win32, same as the testhost case, and there is
no `-e` flag for `run`) — fix it in the csproj, not the shell.

## Running the Windows .NET SDK from WSL

There is **no `dotnet` on the WSL `PATH`** — the SDK is the Windows install. Drive it from WSL via
the Windows host binary (mind the space in the path — quote it):

```bash
# stay on the real C: drive mount, NOT a \\wsl$\ UNC path (UNC breaks protobuf/codegen — see
# the simrace-build-env note); /mnt/c/Users/koba9/ai-simrace maps to C:\Users\koba9\ai-simrace.
cd /mnt/c/Users/koba9/ai-simrace
"/mnt/c/Program Files/dotnet/dotnet.exe" --version          # 10.x here; net9 runtime NOT installed
"/mnt/c/Program Files/dotnet/dotnet.exe" build SimCoach.sln -c Debug
"/mnt/c/Program Files/dotnet/dotnet.exe" format SimCoach.sln --verify-no-changes
```

**`dotnet test` roll-forward gotcha is worse from WSL.** The net9.0 VSTest `testhost.exe` needs the
roll-forward env var (previous section), but a **shell-prefix env var set in WSL does NOT reach the
Windows testhost** — it doesn't cross the WSL→Win32 boundary. Both of these silently fail with "You
must install or update .NET", version 9.0.0:

```bash
DOTNET_ROLL_FORWARD=Major "…/dotnet.exe" test …                 # ❌ var lost crossing to Windows
WSLENV=DOTNET_ROLL_FORWARD/u DOTNET_ROLL_FORWARD=Major "…" test  # ❌ also didn't propagate to testhost
```

Use `dotnet test`'s own `-e` flag, which injects the variable straight into the test-host process:

```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" test SimCoach.sln -c Debug -e DOTNET_ROLL_FORWARD=Major
```

(Only .NET 6/8/10 runtimes were present — no 9.0 — so the net9.0 testhost must roll forward to 10.)

**An absolute `/mnt/c/...` path as the project/sln argument fails with MSB1001.** The Windows
`dotnet.exe` parses `/mnt/...` as an MSBuild switch ("Unknown switch") — the leading `/` looks like
a flag. A *relative* arg from cwd works (`… build SimCoach.sln`), or pass a Windows-style path:

```bash
"/mnt/c/Program Files/dotnet/dotnet.exe" build 'C:\Users\koba9\ai-simrace\SimCoach.sln'
"/mnt/c/Program Files/dotnet/dotnet.exe" test  'C:\Users\koba9\ai-simrace\SimCoach.sln' -e DOTNET_ROLL_FORWARD=Major
```

Same rule for any tool that shells to the Windows dotnet: the ground-truth dumper/xUnit run as
**Windows** processes (Windows paths, and env vars via `test -e`, not a shell prefix), while the
Python oracle runs in **WSL** (`/mnt/c` paths). See `docs/05-implementation/ground-truth-revalidation.md`.

**A Windows tool exe cannot write to the WSL session scratchpad.** The Claude scratchpad
(`/tmp/claude-.../scratchpad`) is a WSL-only path with no Windows drive letter, so passing it as the
dumper's output arg fails (the Windows process can't resolve it). Route the output through a Windows
temp dir and read it back from WSL via the `/mnt/c` mount — the two names point at the same file:

```bash
"…/dotnet.exe" run --project tools/SimCoach.GroundTruthDump -c Release -- \
  'C:\Users\koba9\AppData\Local\SimCoach\recordings\<sessionId>' \
  'C:\Users\koba9\AppData\Local\Temp\dump.csv'          # Windows write target
python3 -c "import pandas; print(len(pandas.read_csv('/mnt/c/Users/koba9/AppData/Local/Temp/dump.csv')))"
```

## SDK 10+ `dotnet new sln` creates `.slnx` by default

`scripts/bootstrap.sh` / `.ps1` pass `--format sln` (with fallback for older SDKs that
don't know the option). `SimCoach.sln` is committed, so the create path rarely runs.
Keep the classic `.sln`: `dotnet format` on SDK 9 is unreliable with `.slnx`.

## `.editorconfig` forbids `var` for non-apparent types — build error, not warning

`TreatWarningsAsErrors` + `csharp_style_var_elsewhere = false` means
`var builder = Host.CreateApplicationBuilder(...)` fails the build with IDE0008.
Use explicit types (`HostApplicationBuilder`, `IHost`) unless the type is apparent
(`var x = new Foo()` is fine).

The inverse also bites: `csharp_style_var_when_type_is_apparent = true` makes an explicit type
a build **error (IDE0007)** when the type is apparent — and a factory/conversion method whose
name names the type counts as apparent. So `DateTimeOffset t = frame.T.ToDateTimeOffset();` fails
(must be `var t = ...`), even though the return type isn't visible at the call site. `csharp_style_var_for_built_in_types = false` exempts built-ins, so `int n = (int)x.TotalMilliseconds;`
stays explicit. Rule of thumb here: `new`/`ToXxx()`/cast on a non-built-in ⇒ `var`; everything
else ⇒ explicit.

The apparent/non-apparent split is finer than "does the RHS name a type": a **factory method whose
name is the type** counts as apparent (`var doc = CenterlineGeometryDocument.FromCenterline(x);` — IDE0007
if you write it explicit), but a **generic method whose type is only in the type argument** does NOT
(`CenterlineGeometryDocument? read = JsonSerializer.Deserialize<CenterlineGeometryDocument>(json);` must be
explicit — `var` fails IDE0008). So `Type.FromX()` ⇒ `var`, `Method<Type>()` ⇒ explicit.

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

## windows-latest CI fails `dotnet format` with mass ENDOFLINE — needs `.gitattributes`

The `actions/checkout` step on the windows-latest runner uses git's default
`core.autocrlf=true`, so every text file is rewritten to CRLF in the working tree. The CI
"Verify formatting" step (`dotnet format --verify-no-changes`) then fails against
`.editorconfig`'s `end_of_line = lf` — hundreds of `ENDOFLINE` errors plus `WHITESPACE` on
wrapped continuation lines (the message "Replace 14 characters with '\n␣␣…'" = `\r\n` + 12
spaces wanting `\n` + 12 spaces; it's the same CRLF, not real indent drift). macOS/Linux pass
because checkout keeps LF there.

Fix is a committed `.gitattributes` forcing LF on checkout everywhere:

```gitattributes
* text=auto eol=lf
*.bin binary
```

Not a code-formatting problem — verify locally with `dotnet format SimCoach.sln
--verify-no-changes` on an LF working tree (it passes), and the diff is purely the checkout EOL.

## Single-file publish: Serilog config needs an explicit `Using` list

A self-contained **single-file** `SimCoach.App.exe` (the CI publish artifact, ADR-0009) crashed
at startup with `InvalidOperationException` from `Serilog.Settings.Configuration` during the
host/DI build — but ran fine under `dotnet run`. Root cause: `ReadFrom.Configuration` discovers
sink/enricher assemblies by **scanning DLLs on disk**; a single-file bundle has none, so the
discovery path throws (not a graceful skip).

Two parts to the fix in `appsettings.json`:

- Add a `"Using"` array so sinks load by assembly name (no disk scan):
  `"Serilog": { "Using": ["Serilog.Sinks.Console", "Serilog.Sinks.File"], ... }`.
  This is the documented single-file workaround.
- The `Enrich` list referenced `WithMachineName` / `WithThreadId`, whose packages
  (`Serilog.Enrichers.Environment` / `.Thread`) were **never referenced** — under `dotnet run`
  they were silently skipped (SelfLog), but in single-file the scan to resolve them throws.
  Dropped them; `FromLogContext` stays (it lives in `Serilog.dll`, always loaded, no scan).

Because `appsettings.json` ships loose next to the exe, an already-downloaded build can be fixed
by hand-editing its `appsettings.json` — no rebuild needed. Trimming is unrelated here (we don't
trim), but if it were ever enabled the sink assemblies would also need a trimmer roots entry.

## `.gitignore` `data/` swallows vendored embedded resources — needs a negation

`.gitignore` has a generic `data/` rule (plus `*.parquet`, `*.mcap`) for runtime output. It also
matches `src/SimCoach.Reference/Data/` (git path-matches `data/` against any `Data/` dir;
`core.ignorecase` on macOS makes the case match too). A vendored file committed as an
`<EmbeddedResource>` there (e.g. `Data/cornerGeometry.json`) is silently untracked — local
builds pass (the file is on disk) but CI checks out without it and the embedded-resource load fails.

`git status` won't list the file; confirm with `git check-ignore -v <path>`. Fix is an explicit
negation after the `data/` block in `.gitignore`:

```gitignore
!src/SimCoach.Reference/Data/
!src/SimCoach.Reference/Data/**
```

Both lines are needed: the first re-includes the directory so git descends into it, the second
re-includes its files.

**Vendoring a NEW embedded-asset family needs its OWN csproj `<EmbeddedResource>` glob — three
things, not two.** `SimCoach.Reference.csproj` embeds `Data\cornerGeometry.*.json`; adding a second
family (`Data\centerline.*.json`) requires a *separate* `<EmbeddedResource Include="Data\centerline.*.json" />`
line — the existing cornerGeometry glob does not match it. Miss the glob and the failure is **silent**:
the loader scans `assembly.GetManifestResourceNames()` for `.Data.centerline`, finds nothing, and
`CenterlineGeometryDataset.Load()` returns an empty dataset — no error, no test failure (unit tests use
the in-memory `FromDocuments` seam, never the embedded `Load()` path), and the whole feature degrades to
its fallback. The full checklist to vendor an embedded asset: (1) `.gitignore` negation (above),
(2) DI/consumer wiring, (3) the csproj glob, **and** (4) a test over the real `Load()` embedded path so a
missing glob fails CI. All three of (1)–(3) are independent; having two of them silently disables the feature.

## NuGet packages that do not exist (verified against nuget.org)

- **MCAP**: no C# package at all (`Mcap.Core` was a scaffold placeholder, removed).
  Minimal MCAP writer is hand-rolled in `SimCoach.Storage` per ADR-0003 risk mitigation.
- **ParquetSharp 16.0.0**: version skipped upstream; pinned to `16.1.0` in
  `Directory.Packages.props`. With `TreatWarningsAsErrors`, a missing exact version
  surfaces as NU1603 *error*, not a warning.
