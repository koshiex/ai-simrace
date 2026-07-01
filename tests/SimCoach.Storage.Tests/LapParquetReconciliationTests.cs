using FluentAssertions;
using Xunit;

namespace SimCoach.Storage.Tests;

public sealed class LapParquetReconciliationTests
{
    [Fact]
    public void Matching_sets_report_no_desync()
    {
        var parquet = new LapParquetWriteResult([1, 2, 3], []);

        (IReadOnlyList<int> onlyInDb, IReadOnlyList<int> onlyInParquet) =
            LapParquetReconciliation.Diff([1, 2, 3], parquet);

        onlyInDb.Should().BeEmpty();
        onlyInParquet.Should().BeEmpty();
    }

    [Fact]
    public void Degenerate_skipped_lap_does_not_false_alarm()
    {
        // The live path keeps every bounded lap; the parquet path skips a degenerate one (lap 3). The
        // union written ∪ skipped equals the laps table, so this must reconcile cleanly — no warning.
        var parquet = new LapParquetWriteResult(WrittenLapNumbers: [2, 4], SkippedLapNumbers: [3]);

        (IReadOnlyList<int> onlyInDb, IReadOnlyList<int> onlyInParquet) =
            LapParquetReconciliation.Diff([2, 3, 4], parquet);

        onlyInDb.Should().BeEmpty();
        onlyInParquet.Should().BeEmpty();
    }

    [Fact]
    public void Equal_count_but_different_labels_is_detected()
    {
        // The failure a count check is blind to: same cardinality, divergent labels (an offset shift
        // from a dropped boundary lap after a pit reset). The set canary must catch it.
        var parquet = new LapParquetWriteResult(WrittenLapNumbers: [1, 2, 4], SkippedLapNumbers: []);

        (IReadOnlyList<int> onlyInDb, IReadOnlyList<int> onlyInParquet) =
            LapParquetReconciliation.Diff([1, 2, 3], parquet);

        onlyInDb.Should().Equal(3);
        onlyInParquet.Should().Equal(4);
    }

    [Fact]
    public void Missing_parquet_lap_is_reported_only_in_db()
    {
        // A swallowed live insert leaves the lap in the DB short, or a lost parquet lap leaves it missing
        // there — either way the difference is surfaced on the correct side.
        var parquet = new LapParquetWriteResult(WrittenLapNumbers: [1, 2], SkippedLapNumbers: []);

        (IReadOnlyList<int> onlyInDb, IReadOnlyList<int> onlyInParquet) =
            LapParquetReconciliation.Diff([1, 2, 3], parquet);

        onlyInDb.Should().Equal(3);
        onlyInParquet.Should().BeEmpty();
    }
}
