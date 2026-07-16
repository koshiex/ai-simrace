using FluentAssertions;
using SimCoach.Reference;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// Persistence half of PR-B3 commit 21: <see cref="AlienReferenceWriter"/> writes a LINE-only parquet and
/// upserts an <c>alien_line</c> row that (a) reads back by kind, (b) coexists with a <c>pb</c> row on the
/// same triple (both resolve — ADR-0021 uniqueness includes <c>kind</c>), (c) satisfies migration-007's
/// CHECKs (non-null <c>parquet_path</c>, null <c>optimal_sector_ms</c>), and (d) round-trips to a LINE-only
/// lap with the seam sentinel intact. Uses a temp-file SQLite DB migrated to latest and a temp references dir.
/// </summary>
public sealed class AlienPersistTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly GhostImportOptions _options = new();

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "simcoach-alien-persist-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory;
    private readonly ReferenceRepository _references;

    public AlienPersistTests()
    {
        _factory = new SqliteConnectionFactory(
            new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") });
        new DatabaseMigrator(_factory).Migrate();
        _references = new ReferenceRepository(_factory);
    }

    private static ReferenceTriple OwnerTriple => new("monza", "bmw_m4_gt3", "dry-warm");

    private static GhostProvenance Provenance => GhostProvenance.FromAccReplay(
        new AccReplayLap(3010828, "ferrari_296_gt3", 100030), "monza");

    [Fact]
    public void Persist_writes_an_alien_line_row_readable_by_kind()
    {
        string parquetPath = Persist();

        ReferenceRow? row = _references.GetByTriple("monza", "bmw_m4_gt3", "dry-warm", "alien_line");
        row.Should().NotBeNull();
        row!.Kind.Should().Be("alien_line");
        row.ParquetPath.Should().Be(parquetPath);
        row.LapTimeMs.Should().Be(100030);
        row.SourceSessionId.Should().BeNull();
        row.SourceLapNumber.Should().BeNull();
        row.OptimalSectorMs.Should().BeNull();
        row.SectorSourcesJson.Should().NotBeNullOrWhiteSpace();
        row.SectorSourcesJson.Should().Contain("ferrari_296_gt3").And.Contain("3010828");
    }

    [Fact]
    public void Alien_line_coexists_with_a_pb_row_on_the_same_triple()
    {
        // A pb row already exists for the owner triple; the alien_line row must not collide with it.
        _references.Upsert(new ReferenceRow
        {
            Id = Guid.NewGuid().ToString("N"),
            TrackId = "monza",
            CarId = "bmw_m4_gt3",
            WeatherBucket = "dry-warm",
            LapTimeMs = 106000,
            ParquetPath = Path.Combine(_root, "references", "monza_bmw_m4_gt3_dry-warm.parquet"),
            CreatedAtUtc = _now,
            Kind = "pb",
        });

        Persist();

        _references.GetByTriple("monza", "bmw_m4_gt3", "dry-warm", "pb")!.LapTimeMs.Should().Be(106000);
        _references.GetByTriple("monza", "bmw_m4_gt3", "dry-warm", "alien_line")!.LapTimeMs.Should().Be(100030);
    }

    [Fact]
    public void Persisted_parquet_round_trips_to_a_line_only_lap_with_the_seam_sentinel()
    {
        string parquetPath = Persist();

        ResampledLap read = ReferenceParquetCodec.Read(parquetPath);

        // Mid-lap bin keeps real world coordinates; the seam bins (pn 0.0 and 0.95) stay NaN.
        read.GridLength.Should().Be(3);
        read.WorldX[1].Should().Be(3f);
        read.WorldZ[1].Should().Be(30f);
        float.IsNaN(read.WorldX[0]).Should().BeTrue();
        float.IsNaN(read.WorldX[2]).Should().BeTrue();
        // LINE-only: every non-line channel is zero.
        read.SpeedMps.Should().OnlyContain(v => v == 0f);
        read.TMsFromLapStart.Should().OnlyContain(v => v == 0);
        read.BrakePct.Should().OnlyContain(v => v == 0f);
        read.ThrottlePct.Should().OnlyContain(v => v == 0f);
    }

    private string Persist() => AlienReferenceWriter.Persist(
        _references,
        DataRootResolver.ReferencesDirectory(_root),
        OwnerTriple,
        SeamMask.Apply(MaskableLine(), _options.SeamBands),
        lapTimeMs: 100030,
        Provenance,
        _now);

    // A three-bin LINE lap: pn 0.0 (seam), 0.5 (real), 0.95 (seam) so SeamMask NaNs bins 0 and 2.
    private static ResampledLap MaskableLine()
    {
        float[] positions = [0.0f, 0.5f, 0.95f];
        float[] worldX = [1f, 3f, 5f];
        float[] worldZ = [10f, 30f, 50f];
        int n = positions.Length;
        return new ResampledLap
        {
            LapNumber = 1,
            GridLength = n,
            PositionNormalized = positions,
            TMsFromLapStart = new int[n],
            SpeedMps = new float[n],
            ThrottlePct = new float[n],
            BrakePct = new float[n],
            SteerRad = new float[n],
            Gear = new int[n],
            TyreTempFl = new float[n],
            TyreTempFr = new float[n],
            TyreTempRl = new float[n],
            TyreTempRr = new float[n],
            GLat = new float[n],
            GLong = new float[n],
            WorldX = worldX,
            WorldY = new float[n],
            WorldZ = worldZ,
        };
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
