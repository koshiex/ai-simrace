# ADR-0001: Use C#/.NET 9 monorepo

**Status**: Accepted
**Date**: 2026-06-01

## Context

Solo developer, Windows-only target, real-time requirements, multiple integration points (game shared memory, audio playback, transparent overlay, HTTP/gRPC, ONNX inference, SQLite/Parquet I/O).

Candidates considered:
- **C#/.NET 9 monorepo**
- **Python core + .NET overlay via gRPC** (better ML libs)
- **Rust core + Tauri overlay** (best perf, single binary)
- **TypeScript/Node + Electron overlay** (fastest UI dev)

## Decision

C#/.NET 9 monorepo.

## Why

- **Single language, single toolchain**. Solo dev productivity dominates over micro-optimisation.
- **First-class Windows interop** (`MemoryMappedFile`, P/Invoke, WinUI/Avalonia, NAudio WASAPI) without FFI bridges.
- **`Microsoft.ML.OnnxRuntime`** is the C# Silero ONNX path.
- **Plain `HttpClient`** suffices for OpenRouter; SSE streaming via `System.Net.ServerSentEvents`.
- **Apache.Arrow.NET + ParquetSharp** for cold storage. `Microsoft.Data.Sqlite` for metadata.
- **Generic Host** gives DI, lifetime, logging, configuration out of the box.
- **`dotnet publish --self-contained`** produces an installer-friendly tree on Windows; Velopack ships updates.
- **AOT-eligible** if cold start becomes an issue later.

## Tradeoffs

- ML/data-science library ecosystem is smaller than Python's. We work around this by keeping all ML at inference time (ONNX) and offloading data analysis to MCAP→Parquet → ad-hoc Python/Polars notebooks where needed.
- Some niche libraries (e.g., `pyaccsharedmemory`) exist only in Python. We re-implement the struct layouts in C# (well-documented by Kunos).

## Consequences

- Both `src/` and `tests/` projects use SDK-style csproj.
- Code style follows `docs/06-style/coding-conventions.md`.
- All long-running services use `IHostedService`.
- Cross-language sidecars (e.g., Python) are forbidden unless we lose a critical capability.
