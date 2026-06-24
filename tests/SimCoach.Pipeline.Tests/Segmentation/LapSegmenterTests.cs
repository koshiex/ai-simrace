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
    public void Lap_counter_bumps_without_a_position_wrap_are_never_boundaries()
    {
        // Arrange — the crossing trigger is the position wrap, not lap_number. A rising position with
        // lap_number bumping every frame (a counter glitch, or just no completed lap) closes nothing.
        LapSegmenter segmenter = new();
        TelemetryFrame[] frames =
        [
            Frame(lapNumber: 1, pos: 0.20f, ms: 0),
            Frame(lapNumber: 2, pos: 0.40f, ms: 10),  // lap_number bumps but position keeps rising
            Frame(lapNumber: 3, pos: 0.60f, ms: 20),
            Frame(lapNumber: 4, pos: 0.80f, ms: 30),
        ];

        // Act
        List<CompletedLap> completed = [.. frames.Select(segmenter.Accept).Where(l => l is not null).Select(l => l!)];

        // Assert
        completed.Should().BeEmpty("no position wrap occurred, so lap_number changes alone are not crossings");
    }

    [Fact]
    public void Acc_desync_lap_bump_one_frame_before_position_wrap_still_closes_lap()
    {
        // Arrange — the real live-ACC signature (Monza capture): completedLaps increments ~1 frame
        // BEFORE normalized_car_position wraps, pinned at 1.0 on the increment frame; and the
        // out-lap → lap-1 crossing never increments the counter at all. The old "lap-bump AND wrap on
        // the same frame" predicate fired zero times here (whole sessions segmented to 0 laps);
        // wrap-primary must still close the interior laps.
        LapSegmenter segmenter = new();
        TelemetryFrame[] frames =
        [
            // out-lap → lap 1: wrap with NO counter increment
            Frame(lapNumber: 1, pos: 0.95f, ms: 0),
            Frame(lapNumber: 1, pos: 1.00f, ms: 10),
            Frame(lapNumber: 1, pos: 0.02f, ms: 20),   // crossing #1 (start observed) — discarded
            // lap 1 → lap 2: counter bumps a frame early while position is pinned at 1.0, then wraps
            Frame(lapNumber: 1, pos: 0.95f, ms: 30),
            Frame(lapNumber: 2, pos: 1.00f, ms: 40),   // desync: lap_number 1→2 with pos still 1.0
            Frame(lapNumber: 2, pos: 0.02f, ms: 50),   // crossing #2 — closes lap 1
            // lap 2 → lap 3
            Frame(lapNumber: 2, pos: 0.95f, ms: 60),
            Frame(lapNumber: 3, pos: 1.00f, ms: 70),
            Frame(lapNumber: 3, pos: 0.02f, ms: 80),   // crossing #3 — closes lap 2
            Frame(lapNumber: 3, pos: 0.50f, ms: 90),
        ];

        // Act
        List<CompletedLap> completed = [.. frames.Select(segmenter.Accept).Where(l => l is not null).Select(l => l!)];

        // Assert — two interior laps the old predicate dropped entirely; no false reset canary.
        completed.Select(l => l.LapNumber).Should().Equal(1, 2);
        segmenter.SuspiciousResetsIgnored.Should().Be(0);
    }

    [Fact]
    public void Mid_lap_position_reset_mints_no_lap_and_is_flagged_suspicious()
    {
        // Arrange — a pit/teleport (or dropped chunk) resets position from MID-lap, not the lap end.
        // The high-water-mark guard (previous frame must be near 1.0) rejects it, so it cannot inflate
        // lap_count, and it is counted as a suspicious reset for the caller to log.
        LapSegmenter segmenter = new();
        TelemetryFrame[] frames =
        [
            Frame(lapNumber: 1, pos: 0.95f, ms: 0),
            Frame(lapNumber: 1, pos: 0.02f, ms: 10),   // real crossing (start observed)
            Frame(lapNumber: 1, pos: 0.40f, ms: 20),
            Frame(lapNumber: 1, pos: 0.55f, ms: 30),
            Frame(lapNumber: 1, pos: 0.05f, ms: 40),   // mid-lap reset: previous 0.55 ≪ 0.9 → not a crossing
            Frame(lapNumber: 1, pos: 0.25f, ms: 50),
        ];

        // Act
        List<CompletedLap> completed = [.. frames.Select(segmenter.Accept).Where(l => l is not null).Select(l => l!)];

        // Assert — only the discarded start crossing fired; the reset minted nothing and was flagged.
        completed.Should().BeEmpty();
        segmenter.SuspiciousResetsIgnored.Should().Be(1);
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
