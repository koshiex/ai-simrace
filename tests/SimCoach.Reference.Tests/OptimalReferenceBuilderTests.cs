using FluentAssertions;
using SimCoach.Reference;
using SimCoach.Storage.Repositories;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// <see cref="OptimalReferenceBuilder"/> (M46): min-per-sector stitching, the PB-exists / gain-floor /
/// per-sector-outlier guards, and idempotence.
/// </summary>
public sealed class OptimalReferenceBuilderTests
{
    private static readonly OptimalReferenceOptions _options = new();

    private static CleanLapSectors Lap(string session, int lapNumber, int lapTimeMs, params int[] sectors) => new()
    {
        SessionId = session,
        LapNumber = lapNumber,
        LapTimeMs = lapTimeMs,
        SectorTimesMs = sectors,
    };

    // Three clean laps whose per-sector minima each come from a different lap: s1←A, s2←B, s3←C.
    private static IReadOnlyList<CleanLapSectors> MonzaLaps() =>
    [
        Lap("A", 3, 113100, 34000, 44000, 35100),
        Lap("B", 5, 113000, 34200, 43800, 35000),
        Lap("C", 7, 112900, 34100, 43900, 34900),
    ];

    [Fact]
    public void Stitches_min_per_sector_below_pb_with_provenance()
    {
        // Arrange — PB is the best single lap (113000); Σ best sectors is genuinely faster.
        // Act
        OptimalReference? optimal = OptimalReferenceBuilder.Build(MonzaLaps(), pbLapTimeMs: 113000, _options);

        // Assert
        optimal.Should().NotBeNull();
        optimal!.SectorDurationsMs.Should().Equal(34000, 43800, 34900);
        optimal.TargetLapTimeMs.Should().Be(112700); // 300 ms under PB
        optimal.Sources.Should().HaveCount(3);
        optimal.Sources[0].Should().BeEquivalentTo(new SectorBestSource
        {
            SectorIndex = 0,
            DurationMs = 34000,
            SessionId = "A",
            LapNumber = 3,
        });
        optimal.Sources[1].SessionId.Should().Be("B");
        optimal.Sources[2].SessionId.Should().Be("C");
    }

    [Fact]
    public void Returns_null_when_gain_below_floor()
    {
        // Σ best sectors = 112700; a PB only 100 ms slower is under the 150 ms floor → PB already the target.
        OptimalReference? optimal = OptimalReferenceBuilder.Build(MonzaLaps(), pbLapTimeMs: 112800, _options);

        optimal.Should().BeNull();
    }

    [Fact]
    public void Rejects_a_poisoned_sector_best_below_the_distribution()
    {
        // Arrange — a tow-poisoned lap sets an unreachable S1 (30000, ~4 s under the field) that still
        // sums to its own lap time so the cheap sanity filter passes; only the per-sector outlier guard
        // can catch it.
        List<CleanLapSectors> laps = [.. MonzaLaps(), Lap("POISON", 1, 109000, 30000, 44000, 35000)];

        // Act
        OptimalReference? optimal = OptimalReferenceBuilder.Build(laps, pbLapTimeMs: 113000, _options);

        // Assert — S1 best falls back to the plausible 34000 from lap A, NOT the poisoned 30000.
        optimal.Should().NotBeNull();
        optimal!.SectorDurationsMs[0].Should().Be(34000);
        optimal.Sources[0].SessionId.Should().Be("A");
        optimal.TargetLapTimeMs.Should().Be(112700);
    }

    [Fact]
    public void Drops_a_lap_whose_sectors_do_not_sum_to_its_lap_time()
    {
        // Arrange — a lap with a fast S3 (34000) but a lap_time that Σ sectors misses by > tolerance is a
        // timing glitch; its S3 must not set the best.
        List<CleanLapSectors> laps = [.. MonzaLaps(), Lap("GLITCH", 9, 120000, 34050, 43850, 34000)];

        // Act
        OptimalReference? optimal = OptimalReferenceBuilder.Build(laps, pbLapTimeMs: 113000, _options);

        // Assert — S3 best stays at the honest 34900 (lap C), the glitch lap is excluded entirely.
        optimal.Should().NotBeNull();
        optimal!.SectorDurationsMs[2].Should().Be(34900);
        optimal.Sources[2].SessionId.Should().Be("C");
    }

    [Fact]
    public void Is_idempotent_for_identical_input()
    {
        OptimalReference? first = OptimalReferenceBuilder.Build(MonzaLaps(), pbLapTimeMs: 113000, _options);
        OptimalReference? second = OptimalReferenceBuilder.Build(MonzaLaps(), pbLapTimeMs: 113000, _options);

        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public void Returns_null_when_no_pb_reference_exists() =>
        OptimalReferenceBuilder.Build(MonzaLaps(), pbLapTimeMs: null, _options).Should().BeNull();

    [Fact]
    public void Returns_null_when_no_clean_laps_stored() =>
        OptimalReferenceBuilder.Build([], pbLapTimeMs: 113000, _options).Should().BeNull();
}
