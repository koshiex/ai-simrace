using Xunit;

// This assembly mixes real-time background-service timing tests (McapRecorderService / SessionManager,
// gated on real-clock polling with a fixed timeout) with CPU/IO-heavy tests (ParquetSharp round-trips,
// multi-segment MCAP). Running them in parallel starves the timing tests on a 2-core CI runner and they
// time out. Serialize the assembly so the timing tests are never CPU-starved by their neighbours.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
