namespace SimCoach.Storage;

/// <summary>
/// Outcome of a <see cref="LapParquetWriter.Write"/> call. <see cref="WrittenLapNumbers"/> are the
/// lap numbers of the row groups written (one per resampled bounded lap); <see cref="SkippedLapNumbers"/>
/// are the bounded laps dropped as degenerate/non-resampleable. Their union is the full set of bounded
/// laps the replay path observed, which the caller reconciles against the <c>laps</c> table's lap numbers
/// (the live path keeps degenerate laps, so the union — not just the written set — is what matches the
/// rows) to surface a DB↔parquet desync. See <see cref="LapParquetReconciliation"/>.
/// </summary>
public sealed record LapParquetWriteResult(
    IReadOnlyList<int> WrittenLapNumbers,
    IReadOnlyList<int> SkippedLapNumbers)
{
    /// <summary>Number of row groups written (one per resampled bounded lap).</summary>
    public int Written => WrittenLapNumbers.Count;

    /// <summary>Number of bounded laps dropped as degenerate/non-resampleable.</summary>
    public int Skipped => SkippedLapNumbers.Count;
}
