using FluentAssertions;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

public sealed class ReferenceRepositoryTests : RepositoryTestBase
{
    private readonly ReferenceRepository _references = null!;

    public ReferenceRepositoryTests() => _references = new ReferenceRepository(Factory);

    private static ReferenceRow Reference(int lapTimeMs, string parquet) => new()
    {
        Id = Guid.NewGuid().ToString(),
        TrackId = "spa",
        CarId = "synthetic_gt3",
        WeatherBucket = "dry-warm",
        LapTimeMs = lapTimeMs,
        ParquetPath = parquet,
        CreatedAtUtc = Now,
    };

    [Fact]
    public void Upsert_then_get_by_triple_round_trips()
    {
        // Arrange
        ReferenceRow row = Reference(104500, "/references/spa.parquet");

        // Act
        _references.Upsert(row);
        ReferenceRow? read = _references.GetByTriple("spa", "synthetic_gt3", "dry-warm");

        // Assert
        read.Should().BeEquivalentTo(row);
    }

    [Fact]
    public void Upsert_on_same_triple_replaces_not_duplicates()
    {
        // Arrange
        _references.Upsert(Reference(104500, "/references/old.parquet"));

        // Act
        _references.Upsert(Reference(103200, "/references/new.parquet"));
        ReferenceRow? read = _references.GetByTriple("spa", "synthetic_gt3", "dry-warm");

        // Assert — one row per triple (UNIQUE), updated in place.
        read!.LapTimeMs.Should().Be(103200);
        read.ParquetPath.Should().Be("/references/new.parquet");
    }

    [Fact]
    public void Get_by_triple_returns_null_when_absent() =>
        _references.GetByTriple("monza", "x", "wet").Should().BeNull();
}
