# SimCoach — AI Sim Racing Coach

Russian-language AI coach for sim racers. Reads live game telemetry, compares vs your personal best, and tells you where you lose time — by voice in your headphones and a minimalist on-screen overlay.

**MVP target**: Assetto Corsa Competizione (ACC) on Windows.
**Roadmap**: iRacing → Le Mans Ultimate → F1 25.

## Status
Pre-alpha. Project scaffold + design docs only.

## Documentation

- [Product Requirements](docs/01-product/PRD.md)
- [Competitive Analysis](docs/01-product/competitive-analysis.md)
- [Architecture](docs/02-architecture/architecture.md)
- [Architecture Decision Records](docs/02-architecture/adr/)
- [Telemetry Schema](docs/02-architecture/telemetry-schema.md)
- [Action Registry](docs/02-architecture/action-registry.md)
- [Functional Requirements](docs/03-functional/functional-requirements.md)
- [Privacy](docs/04-data/privacy.md)
- [Implementation Plan](docs/05-implementation/implementation-plan.md)
- [Testing Strategy](docs/05-implementation/testing-strategy.md)
- [Coding Conventions](docs/06-style/coding-conventions.md)
- [Prompt Style Guide](docs/06-style/prompt-style-guide.md)

## Architecture (one-line)
`ACC SHM → Protobuf → MCAP capture + deterministic compute → Gold-tier JSON → OpenRouter LLM (Gemini 2.5 Flash / DeepSeek V3.2) → Silero v5 RU TTS → Avalonia transparent overlay + audio.`

## Quick Start (post-scaffold)
1. Install .NET 9 SDK (or newer — `global.json` rolls forward across majors).
2. `./scripts/bootstrap.sh` (macOS/Linux) or `./scripts/bootstrap.ps1` (Windows) — only needed after adding new projects; `SimCoach.sln` is committed.
3. `dotnet build && dotnet test`.
   On a machine with only a newer runtime installed (e.g. .NET 10), run tests with `DOTNET_ROLL_FORWARD=LatestMajor dotnet test` — the VSTest host pins the 9.0 runtime otherwise.
4. Configure API keys in `%APPDATA%/SimCoach/secrets.json` (template in `docs/`).
5. Run `SimCoach.App` while ACC is running (Windows; reads `Local\acpmf_*` shared memory).

## Dev loop without ACC (any OS)

Replay a recorded MCAP session through the full pipeline instead of live shared memory:

```bash
SIMCOACH_Telemetry__Source=replay \
SIMCOACH_Telemetry__Replay__Path=/path/to/recordings/<sessionId> \
dotnet run --project src/SimCoach.App -p:RuntimeIdentifier=osx-arm64
```

`Telemetry:Replay:SpeedMultiplier`: `1` = original timing, `0` = as fast as possible.
Recordings land in `<Storage:DataRoot>/recordings/<sessionId>/segment-NNNN.mcap` and validate
with the official CLI: `mcap doctor <file>`.

## License
TBD.
