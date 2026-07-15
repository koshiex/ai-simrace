using FluentAssertions;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Storage.Tests.Repositories;

/// <summary>
/// <see cref="LapRepository.BestSectorsByTriple"/> (M46): gathers every stored clean lap for a triple with
/// a full sector set, carrying provenance and lap time, and excludes non-clean / partial-sector laps and
/// other triples.
/// </summary>
public sealed class LapRepositoryBestSectorsTests : RepositoryTestBase
{
    private readonly SessionRepository _sessions;
    private readonly LapRepository _laps;

    public LapRepositoryBestSectorsTests()
    {
        _sessions = new SessionRepository(Factory);
        _laps = new LapRepository(Factory);
    }

    private SessionRow SessionFor(string id, string track, string car, string weather) => new()
    {
        Id = id,
        StartedAtUtc = Now,
        Sim = "acc",
        TrackId = track,
        CarId = car,
        WeatherBucket = weather,
        McapPath = $"/recordings/{id}",
    };

    private void InsertLap(
        string id, string sessionId, int lapNumber, int lapTimeMs,
        bool clean, int? s1, int? s2, int? s3) =>
        _laps.Insert(new LapRow
        {
            Id = id,
            SessionId = sessionId,
            LapNumber = lapNumber,
            LapTimeMs = lapTimeMs,
            IsClean = clean,
            S1Ms = s1,
            S2Ms = s2,
            S3Ms = s3,
        });

    [Fact]
    public void Returns_clean_full_sector_laps_across_sessions_with_provenance()
    {
        // Arrange — two sessions on the same triple, each with one clean full-sector lap.
        _sessions.Insert(SessionFor("s1", "monza", "bmw_m4_gt3", "dry-warm"));
        _sessions.Insert(SessionFor("s2", "monza", "bmw_m4_gt3", "dry-warm"));
        InsertLap("l1", "s1", 3, 113000, clean: true, 34000, 44000, 35000);
        InsertLap("l2", "s2", 5, 112500, clean: true, 33800, 43900, 34800);

        // Act
        IReadOnlyList<CleanLapSectors> read = _laps.BestSectorsByTriple("monza", "bmw_m4_gt3", "dry-warm");

        // Assert
        read.Should().HaveCount(2);
        read[0].Should().BeEquivalentTo(new CleanLapSectors
        {
            SessionId = "s1",
            LapNumber = 3,
            LapTimeMs = 113000,
            SectorTimesMs = new[] { 34000, 44000, 35000 },
        });
        read[1].SessionId.Should().Be("s2");
        read[1].LapNumber.Should().Be(5);
        read[1].SectorTimesMs.Should().Equal(33800, 43900, 34800);
    }

    [Fact]
    public void Excludes_non_clean_partial_sector_and_other_triple_laps()
    {
        // Arrange
        _sessions.Insert(SessionFor("s1", "monza", "bmw_m4_gt3", "dry-warm"));
        _sessions.Insert(SessionFor("other", "spa", "bmw_m4_gt3", "dry-warm"));
        InsertLap("clean", "s1", 1, 113000, clean: true, 34000, 44000, 35000);
        InsertLap("dirty", "s1", 2, 112000, clean: false, 33500, 43800, 34700);   // not clean
        InsertLap("partial", "s1", 3, 113000, clean: true, 34000, null, 35000);   // missing sector
        InsertLap("wrongtrack", "other", 1, 111000, clean: true, 33000, 43000, 35000); // different triple

        // Act
        IReadOnlyList<CleanLapSectors> read = _laps.BestSectorsByTriple("monza", "bmw_m4_gt3", "dry-warm");

        // Assert — only the single clean, full-sector, same-triple lap survives.
        read.Should().ContainSingle();
        read[0].SessionId.Should().Be("s1");
        read[0].LapNumber.Should().Be(1);
    }

    [Fact]
    public void Returns_empty_when_no_clean_laps_for_triple() =>
        _laps.BestSectorsByTriple("nürburgring", "bmw_m4_gt3", "wet").Should().BeEmpty();
}
