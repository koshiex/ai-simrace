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
/// <see cref="OptimalReferenceBaker"/> (M46): the StartAsync catch-up derives a row-only optimal from
/// seeded historical clean laps + a stored PB, is idempotent across repeated starts, and writes nothing
/// when no PB exists or the gain over PB is below the floor.
/// </summary>
public sealed class OptimalReferenceBakerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "simcoach-baker-" + Guid.NewGuid().ToString("N"));
    private readonly SqliteConnectionFactory _factory;
    private readonly SessionRepository _sessions;
    private readonly LapRepository _laps;
    private readonly ReferenceRepository _references;

    private static readonly ReferenceTriple _triple = new("monza", "bmw_m4_gt3", "dry-warm");
    private static readonly DateTimeOffset _now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    public OptimalReferenceBakerTests()
    {
        Directory.CreateDirectory(_root);
        _factory = new SqliteConnectionFactory(new DatabaseOptions { DbPath = Path.Combine(_root, "simcoach.db") });
        new DatabaseMigrator(_factory).Migrate();
        _sessions = new SessionRepository(_factory);
        _laps = new LapRepository(_factory);
        _references = new ReferenceRepository(_factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private OptimalReferenceBaker NewBaker() => new(
        _references,
        _laps,
        new OptimalReferenceOptions(),
        TimeProvider.System,
        NullLogger<OptimalReferenceBaker>.Instance);

    private void SeedSession(string id) => _sessions.Insert(new SessionRow
    {
        Id = id,
        StartedAtUtc = _now,
        Sim = "acc",
        TrackId = _triple.TrackId,
        CarId = _triple.CarId,
        WeatherBucket = _triple.WeatherBucket,
        McapPath = $"/recordings/{id}",
    });

    private void SeedLap(string id, string sessionId, int lapNumber, int lapTimeMs, int s1, int s2, int s3) =>
        _laps.Insert(new LapRow
        {
            Id = id,
            SessionId = sessionId,
            LapNumber = lapNumber,
            LapTimeMs = lapTimeMs,
            IsClean = true,
            S1Ms = s1,
            S2Ms = s2,
            S3Ms = s3,
        });

    private void SeedPb(int lapTimeMs) => _references.Upsert(new ReferenceRow
    {
        Id = Guid.NewGuid().ToString(),
        TrackId = _triple.TrackId,
        CarId = _triple.CarId,
        WeatherBucket = _triple.WeatherBucket,
        LapTimeMs = lapTimeMs,
        ParquetPath = $"/references/{_triple.ParquetFileName}",
        CreatedAtUtc = _now,
        Kind = "pb",
    });

    // Three clean laps whose per-sector minima come from different laps: s1←34000, s2←43800, s3←34900.
    private void SeedMonzaLaps()
    {
        SeedSession("s1");
        SeedSession("s2");
        SeedLap("l1", "s1", 3, 113100, 34000, 44000, 35100);
        SeedLap("l2", "s1", 4, 113000, 34200, 43800, 35000);
        SeedLap("l3", "s2", 5, 112900, 34100, 43900, 34900);
    }

    private ReferenceRow? Optimal() =>
        _references.GetByTriple(_triple.TrackId, _triple.CarId, _triple.WeatherBucket, "optimal");

    [Fact]
    public async Task Catch_up_bakes_an_optimal_from_historical_laps_and_pb()
    {
        // Arrange — PB is the best single lap; Σ best sectors (112700) is genuinely faster.
        SeedMonzaLaps();
        SeedPb(113000);

        // Act
        await NewBaker().StartAsync(CancellationToken.None);

        // Assert — a row-only optimal row appears with the stitched target and provenance.
        ReferenceRow? optimal = Optimal();
        optimal.Should().NotBeNull();
        optimal!.Kind.Should().Be("optimal");
        optimal.ParquetPath.Should().BeNull();
        optimal.LapTimeMs.Should().Be(112700);
        JsonSerializer.Deserialize<int[]>(optimal.OptimalSectorMs!).Should().Equal(34000, 43800, 34900);
        optimal.SectorSourcesJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Catch_up_is_idempotent_across_repeated_starts()
    {
        // Arrange
        SeedMonzaLaps();
        SeedPb(113000);

        // Act — bake twice.
        await NewBaker().StartAsync(CancellationToken.None);
        ReferenceRow first = Optimal()!;
        await NewBaker().StartAsync(CancellationToken.None);
        ReferenceRow second = Optimal()!;

        // Assert — still exactly one optimal, same id + durations, unchanged created_at (no churn).
        _references.GetAllByKind("optimal").Should().ContainSingle();
        second.Id.Should().Be(first.Id);
        second.OptimalSectorMs.Should().Be(first.OptimalSectorMs);
        second.CreatedAtUtc.Should().Be(first.CreatedAtUtc);
    }

    [Fact]
    public async Task Writes_nothing_when_no_pb_reference_exists()
    {
        // Arrange — clean laps, but no PB reference to express a gain against.
        SeedMonzaLaps();

        // Act
        await NewBaker().StartAsync(CancellationToken.None);

        // Assert
        Optimal().Should().BeNull();
    }

    [Fact]
    public async Task Writes_nothing_when_gain_over_pb_is_below_the_floor()
    {
        // Arrange — Σ best sectors is 112700; a PB of 112800 leaves a 100 ms gain, below the 150 ms floor.
        SeedMonzaLaps();
        SeedPb(112800);

        // Act
        await NewBaker().StartAsync(CancellationToken.None);

        // Assert
        Optimal().Should().BeNull();
    }
}
