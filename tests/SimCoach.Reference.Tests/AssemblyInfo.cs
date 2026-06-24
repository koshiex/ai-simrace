using Xunit;

// Mirrors SimCoach.Storage.Tests: this assembly mixes a real-time background-service e2e
// (Phase2ComputeE2EGoldenTests drives the full replay→ingest→compute chain on real-clock polling with
// a fixed timeout) with CPU/IO-heavy SQLite + ParquetSharp tests (ComputeTestHarness opens a temp DB
// per test). Running in parallel both starves the timing-sensitive e2e on a 2-core CI runner and locks
// the temp `simcoach.db` files on Windows (pooled connections vs Directory.Delete). Serialize the
// assembly so neither happens.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
