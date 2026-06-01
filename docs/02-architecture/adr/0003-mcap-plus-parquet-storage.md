# ADR-0003: MCAP for live capture, Parquet for cold storage

**Status**: Accepted
**Date**: 2026-06-01

## Context

We capture 100–333 Hz multi-channel telemetry plus emitted events. We need:
- Write-optimised, crash-safe live capture (the game can crash mid-lap).
- Heterogeneous schemas (TelemetryFrame, CornerEvent, SectorEvent, LapEvent, SessionMeta).
- Fast per-lap seeking for the reference store.
- Long-term storage that compresses well and is column-queryable for analytics.

Candidates: raw CSV, JSONL, gzipped binary, InfluxDB line protocol, MCAP, Parquet, ROS bag.

## Decision

- **Live capture**: **MCAP** with zstd chunk compression, protobuf-encoded messages, rotated every 60 seconds. Industry-standard for high-rate multi-channel time-series since ROS 2 Iron made it the default.
- **Cold storage / reference**: **Parquet**, partitioned by `(driver, track, car, weather)`, one row group per lap. Channels resampled to 1m of `normalizedCarPosition` for fast delta computation.
- **Metadata**: **SQLite** for sessions, laps, references index, settings, LLM usage.

## Why

- MCAP supports crash-safe append, in-file indexes for seek-by-time/seek-by-channel, multiple schemas in one file.
- C# bindings exist (`mcap` NuGet from foxglove/mcap repo).
- Parquet is the lingua franca for analytics; lets users open recorded data in DuckDB / Polars / pandas without our app.
- SQLite is zero-config, atomic, single-file, perfect for a desktop app.

## Tradeoffs

- Two write paths (MCAP + Parquet) instead of one. We mitigate by writing MCAP only at capture time, and converting MCAP → Parquet asynchronously at session end (and on-demand later).
- Disk space: an hour of ACC at 333 Hz compressed is ~50–100 MB. Acceptable for a sim racer.

## Consequences

- `Storage.MCAP` namespace handles writes only; reads happen via the `mcap` library for the replay tool.
- `Storage.Parquet` namespace handles end-of-session conversion + reference snapshots.
- `Storage.Sqlite` handles metadata + LLM cost meter.
