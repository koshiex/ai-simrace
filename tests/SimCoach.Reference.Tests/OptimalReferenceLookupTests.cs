using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using SimCoach.Reference;
using SimCoach.Storage.Database;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// <see cref="OptimalReferenceLookup"/> (M46): reads the row-only optimal, prefix-sums its per-sector
/// durations to cumulative boundaries, and fails fast when the stored length disagrees with the sim's
/// sector count.
/// </summary>
public sealed class OptimalReferenceLookupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "simcoach-optimal-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory;
    private readonly ReferenceRepository _references;
    private readonly OptimalReferenceLookup _lookup;
    private static readonly ReferenceTriple _triple = new("monza", "bmw_m4_gt3", "dry-warm");

    public OptimalReferenceLookupTests()
    {
        Directory.CreateDirectory(_root);
        _factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") });
        new DatabaseMigrator(_factory).Migrate();
        _references = new ReferenceRepository(_factory);
        _lookup = new OptimalReferenceLookup(_references, NullLogger<OptimalReferenceLookup>.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private void UpsertOptimal(string sectorsJson) => _references.Upsert(new ReferenceRow
    {
        Id = Guid.NewGuid().ToString(),
        TrackId = _triple.TrackId,
        CarId = _triple.CarId,
        WeatherBucket = _triple.WeatherBucket,
        LapTimeMs = 112700,
        ParquetPath = null,
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        Kind = "optimal",
        OptimalSectorMs = sectorsJson,
    });

    [Fact]
    public void Prefix_sums_durations_to_cumulative_boundaries()
    {
        // Arrange
        UpsertOptimal(JsonSerializer.Serialize(new[] { 34000, 43800, 34900 }));

        // Act
        int[]? boundaries = _lookup.GetSectorTimes(_triple, expectedSectorCount: 3);

        // Assert — cumulative: [s1, s1+s2, s1+s2+s3].
        boundaries.Should().Equal(34000, 77800, 112700);
    }

    [Fact]
    public void Returns_null_when_no_optimal_stored() =>
        _lookup.GetSectorTimes(_triple, expectedSectorCount: 3).Should().BeNull();

    [Fact]
    public void Throws_when_stored_sector_count_disagrees_with_sim()
    {
        // Arrange — three stored sectors, but the sim reports four.
        UpsertOptimal(JsonSerializer.Serialize(new[] { 34000, 43800, 34900 }));

        // Act
        Action mismatch = () => _lookup.GetSectorTimes(_triple, expectedSectorCount: 4);

        // Assert
        mismatch.Should().Throw<InvalidOperationException>().WithMessage("*3 sectors, expected 4*");
    }
}
