using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Pipeline.Tests.Segmentation;

public sealed class LapSegmenterTests
{
    private static readonly DateTimeOffset _start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Five_lap_fixture_yields_three_fully_bounded_interior_laps()
    {
        // Arrange — the first and last laps are partial (start/end never observed as a crossing).
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 5);

        // Act
        IReadOnlyList<CompletedLap> laps = Segment(frames);

        // Assert
        laps.Select(l => l.LapNumber).Should().Equal(2, 3, 4);
    }

    [Fact]
    public void Sector_times_sum_to_lap_time()
    {
        // Arrange — 100 Hz × 200 samples/lap ⇒ 2000 ms laps; Spa has 3 sectors.
        IReadOnlyList<CompletedLap> laps = Segment(SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4));

        // Assert
        laps.Should().NotBeEmpty();
        foreach (CompletedLap lap in laps)
        {
            lap.LapTimeMs.Should().Be(2000);
            lap.SectorTimesMs.Should().HaveCount(3);
            lap.SectorTimesMs.Sum().Should().Be(lap.LapTimeMs);
        }
    }

    [Fact]
    public void Dirty_lap_is_flagged_not_clean()
    {
        // Arrange — lap 3 is invalid; laps 2 and 4 are clean.
        IReadOnlyList<TelemetryFrame> frames =
            SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 5, dirtyLaps: new HashSet<int> { 3 });

        // Act
        IReadOnlyList<CompletedLap> laps = Segment(frames);

        // Assert
        laps.Single(l => l.LapNumber == 3).IsClean.Should().BeFalse();
        laps.Where(l => l.LapNumber != 3).Should().OnlyContain(l => l.IsClean);
    }

    [Fact]
    public void Lap_number_bump_without_position_wrap_is_not_a_boundary()
    {
        // Arrange — a lap-counter bump while position keeps rising must NOT close a lap. Only the
        // genuine wrap (high → low) does. Sequence: rise, spurious bump (no wrap), real wrap, rise.
        LapSegmenter segmenter = new();
        TelemetryFrame[] frames =
        [
            Frame(lapNumber: 1, pos: 0.10f, ms: 0),
            Frame(lapNumber: 1, pos: 0.60f, ms: 10),
            Frame(lapNumber: 2, pos: 0.90f, ms: 20),  // bump WITHOUT wrap — must be ignored
            Frame(lapNumber: 3, pos: 0.05f, ms: 30),  // real crossing (wrap) — closes the run above
            Frame(lapNumber: 3, pos: 0.50f, ms: 40),
        ];

        // Act
        List<CompletedLap> completed = [.. frames.Select(segmenter.Accept).Where(l => l is not null).Select(l => l!)];

        // Assert — exactly one crossing fired; the spurious bump did not split the lap early.
        completed.Should().BeEmpty(
            "the first lap is partial (its start was never observed) so it is discarded, "
            + "and the spurious bump created no extra boundary");
    }

    private static IReadOnlyList<CompletedLap> Segment(IReadOnlyList<TelemetryFrame> frames)
    {
        LapSegmenter segmenter = new();
        List<CompletedLap> laps = [];
        foreach (TelemetryFrame frame in frames)
        {
            if (segmenter.Accept(frame) is { } lap)
            {
                laps.Add(lap);
            }
        }

        return laps;
    }

    private static TelemetryFrame Frame(int lapNumber, float pos, int ms) => new()
    {
        T = Timestamp.FromDateTimeOffset(_start.AddMilliseconds(ms)),
        LapNumber = lapNumber,
        NormalizedCarPosition = pos,
        CurrentSectorIndex = 0,
        SectorCount = 3,
        IsValidLap = true,
    };
}
