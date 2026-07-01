namespace SimCoach.Storage;

/// <summary>
/// Reconciles the lap numbers in the <c>laps</c> table against those the <c>laps.parquet</c> writer
/// observed. The live compute path and the replay (parquet) path each renumber laps with their own
/// <see cref="Pipeline.Segmentation.LapSegmenter"/> over independent, separately-droppable frame streams
/// (ADR-0015), so after a pit-return counter reset a dropped boundary lap can shift the offset on one
/// side and desync the lap-number sets — a value-level mismatch a mere count check cannot see. The
/// parquet's full bounded-lap set is <c>written ∪ skipped</c> (the live path keeps degenerate laps the
/// parquet skips), so a clean degenerate-lap session reconciles exactly and does not false-alarm.
/// </summary>
public static class LapParquetReconciliation
{
    /// <summary>
    /// Returns the lap numbers present in exactly one source. Both lists empty ⇒ the table and the
    /// parquet agree; a non-empty list signals a DB↔parquet desync. Results are sorted ascending.
    /// </summary>
    public static (IReadOnlyList<int> OnlyInDb, IReadOnlyList<int> OnlyInParquet) Diff(
        IReadOnlyList<int> dbLapNumbers, LapParquetWriteResult parquet)
    {
        ArgumentNullException.ThrowIfNull(dbLapNumbers);
        ArgumentNullException.ThrowIfNull(parquet);

        HashSet<int> db = [.. dbLapNumbers];
        HashSet<int> parquetLaps = [.. parquet.WrittenLapNumbers, .. parquet.SkippedLapNumbers];

        int[] onlyInDb = [.. db.Except(parquetLaps).Order()];
        int[] onlyInParquet = [.. parquetLaps.Except(db).Order()];
        return (onlyInDb, onlyInParquet);
    }
}
