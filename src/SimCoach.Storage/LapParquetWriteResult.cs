namespace SimCoach.Storage;

/// <summary>
/// Outcome of a <see cref="LapParquetWriter.Write"/> call. <see cref="Written"/> is the number of row
/// groups written (one per resampled bounded lap); <see cref="Skipped"/> is the number of bounded laps
/// dropped as degenerate/non-resampleable. Their sum is the bounded-lap count the replay path observed,
/// which the caller compares against the <c>laps</c> table row count to surface a DB↔parquet desync.
/// </summary>
public sealed record LapParquetWriteResult(int Written, int Skipped);
