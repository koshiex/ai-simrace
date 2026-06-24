using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Storage;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class ReferenceStoreTests
{
    private static readonly ReferenceTriple _triple = new("spa", "synthetic_gt3", "dry-warm");
    private static readonly SessionIdentity _identity = new("20260601-120000-000", DateTimeOffset.UnixEpoch);

    [Fact]
    public void First_clean_lap_establishes_a_reference_and_writes_its_parquet()
    {
        using var harness = new ComputeTestHarness();
        harness.SeedSession(_identity.SessionId, _triple);

        bool updated = harness.ReferenceStore.MaybeUpdate(_triple, CleanLap(104_000), Grid(), _identity);

        updated.Should().BeTrue();
        ReferenceRow? row = harness.References.GetByTriple("spa", "synthetic_gt3", "dry-warm");
        row.Should().NotBeNull();
        row!.LapTimeMs.Should().Be(104_000);
        File.Exists(row.ParquetPath).Should().BeTrue();
        harness.Lookup.Get(_triple).Should().NotBeNull();
    }

    [Fact]
    public void A_faster_clean_lap_replaces_the_reference()
    {
        using var harness = new ComputeTestHarness();
        harness.SeedSession(_identity.SessionId, _triple);
        harness.ReferenceStore.MaybeUpdate(_triple, CleanLap(104_000), Grid(), _identity);

        bool updated = harness.ReferenceStore.MaybeUpdate(_triple, CleanLap(102_500), Grid(), _identity);

        updated.Should().BeTrue();
        harness.References.GetByTriple("spa", "synthetic_gt3", "dry-warm")!.LapTimeMs.Should().Be(102_500);
    }

    [Fact]
    public void A_slower_clean_lap_is_rejected()
    {
        using var harness = new ComputeTestHarness();
        harness.SeedSession(_identity.SessionId, _triple);
        harness.ReferenceStore.MaybeUpdate(_triple, CleanLap(102_000), Grid(), _identity);

        bool updated = harness.ReferenceStore.MaybeUpdate(_triple, CleanLap(103_000), Grid(), _identity);

        updated.Should().BeFalse();
        harness.References.GetByTriple("spa", "synthetic_gt3", "dry-warm")!.LapTimeMs.Should().Be(102_000);
    }

    [Fact]
    public void A_pinned_reference_survives_a_faster_lap()
    {
        using var harness = new ComputeTestHarness();
        harness.SeedSession(_identity.SessionId, _triple);
        harness.References.Upsert(new ReferenceRow
        {
            Id = Guid.NewGuid().ToString("N"),
            TrackId = "spa",
            CarId = "synthetic_gt3",
            WeatherBucket = "dry-warm",
            LapTimeMs = 105_000,
            ParquetPath = Path.Combine(harness.ReferencesDirectory, "pinned.parquet"),
            Pinned = true,
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
        });

        bool updated = harness.ReferenceStore.MaybeUpdate(_triple, CleanLap(101_000), Grid(), _identity);

        updated.Should().BeFalse();
        harness.References.GetByTriple("spa", "synthetic_gt3", "dry-warm")!.LapTimeMs.Should().Be(105_000);
    }

    [Fact]
    public void A_dirty_lap_never_becomes_a_reference()
    {
        using var harness = new ComputeTestHarness();
        harness.SeedSession(_identity.SessionId, _triple);

        bool updated = harness.ReferenceStore.MaybeUpdate(_triple, DirtyLap(95_000), Grid(), _identity);

        updated.Should().BeFalse();
        harness.References.GetByTriple("spa", "synthetic_gt3", "dry-warm").Should().BeNull();
    }

    [Fact]
    public void Lookup_returns_null_when_no_reference_exists()
    {
        using var harness = new ComputeTestHarness();
        harness.SeedSession(_identity.SessionId, _triple);

        harness.Lookup.Get(_triple).Should().BeNull();
    }

    private static CompletedLap CleanLap(int lapTimeMs) => Lap(lapTimeMs, isClean: true);

    private static CompletedLap DirtyLap(int lapTimeMs) => Lap(lapTimeMs, isClean: false);

    private static CompletedLap Lap(int lapTimeMs, bool isClean) => new()
    {
        LapNumber = 2,
        LapTimeMs = lapTimeMs,
        SectorTimesMs = [34_000, 35_000, lapTimeMs - 69_000],
        IsClean = isClean,
        Frames = [new TelemetryFrame { T = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch) }],
    };

    private static ResampledLap Grid() => new()
    {
        LapNumber = 2,
        GridLength = 2,
        PositionNormalized = [0f, 1f],
        TMsFromLapStart = [0, 104_000],
        SpeedMps = [70f, 68f],
        ThrottlePct = [1f, 0.9f],
        BrakePct = [0f, 0f],
        SteerRad = [0f, 0f],
        Gear = [4, 4],
        TyreTempFl = [80f, 80f],
        TyreTempFr = [80f, 80f],
        TyreTempRl = [80f, 80f],
        TyreTempRr = [80f, 80f],
        GLat = [0f, 0f],
        GLong = [0f, 0f],
        WorldX = [0f, 1f],
        WorldY = [0f, 0f],
        WorldZ = [0f, 1f],
    };
}
