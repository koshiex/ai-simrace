using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class ReferenceSnapshotRepositoryTests : RepositoryTestBase
{
    private readonly ReferenceSnapshotRepository _snapshots;

    public ReferenceSnapshotRepositoryTests() => _snapshots = new ReferenceSnapshotRepository(Factory);

    private static ReferenceSnapshotRow Snapshot(int lapTimeMs, string parquet, string? sessionId = null, int minute = 0) => new()
    {
        Id = Guid.NewGuid().ToString(),
        TrackId = "spa",
        CarId = "synthetic_gt3",
        WeatherBucket = "dry-warm",
        SourceSessionId = sessionId,
        SourceLapNumber = 7,
        LapTimeMs = lapTimeMs,
        ParquetPath = parquet,
        CreatedAtUtc = Now.AddMinutes(minute),
    };

    [Fact]
    public void Insert_then_list_returns_snapshots_oldest_first()
    {
        _snapshots.Insert(Snapshot(104500, "/references/snap-b.parquet", minute: 5));
        _snapshots.Insert(Snapshot(104900, "/references/snap-a.parquet", minute: 1));

        IReadOnlyList<ReferenceSnapshotRow> all = _snapshots.ListByTriple("spa", "synthetic_gt3", "dry-warm");

        all.Should().HaveCount(2);
        all.Select(s => s.ParquetPath).Should().ContainInOrder(
            "/references/snap-a.parquet", "/references/snap-b.parquet");
    }

    [Fact]
    public void List_by_triple_is_empty_when_absent() =>
        _snapshots.ListByTriple("monza", "x", "wet").Should().BeEmpty();

    [Fact]
    public void Delete_removes_only_the_named_snapshot()
    {
        ReferenceSnapshotRow keep = Snapshot(104_900, "/references/keep.parquet", minute: 1);
        ReferenceSnapshotRow drop = Snapshot(104_500, "/references/drop.parquet", minute: 5);
        _snapshots.Insert(keep);
        _snapshots.Insert(drop);

        _snapshots.Delete(drop.Id);

        IReadOnlyList<ReferenceSnapshotRow> all = _snapshots.ListByTriple("spa", "synthetic_gt3", "dry-warm");
        all.Should().ContainSingle().Which.Id.Should().Be(keep.Id);
    }

    [Fact]
    public void Deleting_the_source_session_nulls_the_fk_but_keeps_the_snapshot()
    {
        // ADR-0017: a snapshot outlives the session that produced it (ON DELETE SET NULL); the parquet
        // history is never orphaned by a session delete.
        using (SqliteConnection connection = Factory.Create())
        {
            connection.Execute(
                "INSERT INTO sessions (id, started_at_utc, sim, track_id, car_id, weather_bucket, mcap_path) "
                + "VALUES ('s1', @now, 'acc', 'spa', 'synthetic_gt3', 'dry-warm', '/rec/s1')",
                new { now = Now });
        }

        _snapshots.Insert(Snapshot(103200, "/references/snap.parquet", sessionId: "s1"));

        using (SqliteConnection connection = Factory.Create())
        {
            connection.Execute("DELETE FROM sessions WHERE id = 's1'");
        }

        IReadOnlyList<ReferenceSnapshotRow> all = _snapshots.ListByTriple("spa", "synthetic_gt3", "dry-warm");
        all.Should().ContainSingle();
        all[0].SourceSessionId.Should().BeNull("ON DELETE SET NULL preserves the snapshot without the session");
    }
}
