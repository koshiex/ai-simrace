using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Storage;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// M4 (PR-B3): the single <see cref="ReferenceLookup"/> is kind-parameterized — the <c>alien_line</c> LINE
/// grid reads through the same <c>Get(triple, kind)</c> path as the <c>pb</c> TIME grid, no second same-type
/// singleton. Covers the kind read, pb/alien coexistence on one triple (ADR-0021 uniqueness includes kind),
/// the InvalidDataException the fault-isolation (M3) must catch, the missing-file degradation, and the M7
/// weather-mismatch finder. The null-parquet-path InvalidOperationException branch is defensive: migration-007's
/// <c>CHECK (kind = 'optimal' OR parquet_path IS NOT NULL)</c> makes it unreachable for a well-migrated DB, so
/// it is not exercised here — the reachable corruption is the multi-row-group parquet.
/// </summary>
public sealed class ReferenceLookupKindTests : IDisposable
{
    private static readonly DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly ReferenceTriple _triple = new("monza", "bmw_m4_gt3", "dry-warm");

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "simcoach-alien-lookup-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory;
    private readonly ReferenceRepository _references;
    private readonly ReferenceLookup _lookup;

    public ReferenceLookupKindTests()
    {
        Directory.CreateDirectory(_root);
        _factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") });
        new DatabaseMigrator(_factory).Migrate();
        _references = new ReferenceRepository(_factory);
        _lookup = new ReferenceLookup(_references, NullLogger<ReferenceLookup>.Instance);
    }

    [Fact]
    public void Get_reads_an_alien_line_row_through_the_kind_parameter()
    {
        UpsertAlien(WriteLineParquet("alien.parquet"), "dry-warm");

        ResampledLap? alien = _lookup.Get(_triple, ReferenceKind.AlienLine);

        alien.Should().NotBeNull();
        alien!.GridLength.Should().Be(3);
        alien.WorldX[1].Should().Be(3f, "the mid bin keeps its real world coordinate");
    }

    [Fact]
    public void Get_defaults_to_pb_and_does_not_resolve_the_alien_line_row()
    {
        UpsertAlien(WriteLineParquet("alien.parquet"), "dry-warm");

        _lookup.Get(_triple).Should().BeNull("no pb row exists; the alien_line row must not answer the default kind");
    }

    [Fact]
    public void Alien_line_and_pb_resolve_independently_on_the_same_triple()
    {
        UpsertPb(WriteLineParquet("pb.parquet"));
        UpsertAlien(WriteLineParquet("alien.parquet"), "dry-warm");

        _lookup.Get(_triple, ReferenceKind.Pb).Should().NotBeNull();
        _lookup.Get(_triple, ReferenceKind.AlienLine).Should().NotBeNull();
    }

    [Fact]
    public void A_multi_row_group_alien_parquet_throws_invalid_data()
    {
        string parquet = Path.Combine(_root, "references", "corrupt.parquet");
        Directory.CreateDirectory(Path.GetDirectoryName(parquet)!);
        CorruptReferenceParquet.WriteMultiRowGroup(parquet);
        UpsertAlien(parquet, "dry-warm");

        Action get = () => _lookup.Get(_triple, ReferenceKind.AlienLine);

        get.Should().Throw<InvalidDataException>("a corrupt import is the exception the alien tier must fault-isolate");
    }

    [Fact]
    public void A_missing_alien_parquet_file_degrades_to_null()
    {
        UpsertAlien(Path.Combine(_root, "references", "does-not-exist.parquet"), "dry-warm");

        _lookup.Get(_triple, ReferenceKind.AlienLine).Should().BeNull();
    }

    [Fact]
    public void FindAlienLineWeatherMismatch_returns_the_stored_bucket_for_a_different_weather()
    {
        UpsertAlien("any-path.parquet", "dry-cool");

        _lookup.FindAlienLineWeatherMismatch(_triple).Should().Be("dry-cool");
    }

    [Fact]
    public void FindAlienLineWeatherMismatch_is_null_when_the_exact_bucket_matches()
    {
        UpsertAlien("any-path.parquet", "dry-warm");

        _lookup.FindAlienLineWeatherMismatch(_triple).Should().BeNull();
    }

    private void UpsertAlien(string parquetPath, string weather) => _references.Upsert(new ReferenceRow
    {
        Id = Guid.NewGuid().ToString("N"),
        TrackId = "monza",
        CarId = "bmw_m4_gt3",
        WeatherBucket = weather,
        LapTimeMs = 100030,
        ParquetPath = parquetPath,
        CreatedAtUtc = _now,
        Kind = "alien_line",
        SectorSourcesJson = "{\"source_car\":\"ferrari_296_gt3\"}",
    });

    private void UpsertPb(string parquetPath) => _references.Upsert(new ReferenceRow
    {
        Id = Guid.NewGuid().ToString("N"),
        TrackId = "monza",
        CarId = "bmw_m4_gt3",
        WeatherBucket = "dry-warm",
        LapTimeMs = 106000,
        ParquetPath = parquetPath,
        CreatedAtUtc = _now,
        Kind = "pb",
    });

    private string WriteLineParquet(string fileName)
    {
        string path = Path.Combine(_root, "references", fileName);
        ReferenceParquetCodec.Write(LineOnlyLap.ThreeBin(), path);
        return path;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
