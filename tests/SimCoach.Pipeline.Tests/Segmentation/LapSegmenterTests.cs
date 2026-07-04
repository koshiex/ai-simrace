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
    public void Lap_counter_bump_with_a_sub_wrap_dip_is_not_a_crossing()
    {
        // The old predicate fired on (lap_number++ AND any backward position step); wrap-primary must
        // NOT — a counter bump with a small dip well short of the lap end is not a start-line crossing.
        // This fails against the old AND-predicate, so it pins the behaviour change.
        LapSegmenter segmenter = new();
        segmenter.Accept(Frame(lapNumber: 1, pos: 0.40f, ms: 0));
        segmenter.Accept(Frame(lapNumber: 2, pos: 0.38f, ms: 10)); // lap++ and a dip, but previous ≪ 0.9

        segmenter.CrossedThisFrame.Should().BeFalse(
            "a lap-counter bump with a sub-wrap position dip is not a wrap from the lap end");
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

    [Fact]
    public void Pit_return_counter_reset_renumbers_laps_into_a_continuous_sequence()
    {
        // The sim's lap counter resets on a pit-return out-lap (…1, 2, [box] 1, 2…), re-issuing a number
        // already completed this session — which would collide on UNIQUE(session_id, lap_number).
        // The segmenter must relabel them into a continuous, strictly-increasing sequence (1, 2, 3, 4).
        LapSegmenter segmenter = new();
        TelemetryFrame[] frames =
        [
            Frame(lapNumber: 1, pos: 0.95f, ms: 0),
            Frame(lapNumber: 1, pos: 0.02f, ms: 10),   // crossing #1 — start observed, discarded
            Frame(lapNumber: 1, pos: 0.95f, ms: 20),
            Frame(lapNumber: 2, pos: 0.02f, ms: 30),   // crossing #2 — closes intrinsic lap 1
            Frame(lapNumber: 2, pos: 0.95f, ms: 40),
            Frame(lapNumber: 1, pos: 0.02f, ms: 50),   // crossing #3 — closes intrinsic lap 2; PIT: counter resets to 1
            Frame(lapNumber: 1, pos: 0.95f, ms: 60),
            Frame(lapNumber: 2, pos: 0.02f, ms: 70),   // crossing #4 — closes the reused intrinsic lap 1
            Frame(lapNumber: 2, pos: 0.95f, ms: 80),
            Frame(lapNumber: 2, pos: 0.02f, ms: 90),   // crossing #5 — closes the reused intrinsic lap 2
            Frame(lapNumber: 2, pos: 0.50f, ms: 100),  // trailing partial — discarded
        ];

        List<CompletedLap> completed = [.. frames.Select(segmenter.Accept).Where(l => l is not null).Select(l => l!)];

        completed.Select(l => l.LapNumber).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Repeated_equal_counter_value_is_still_renumbered_forward()
    {
        // A counter that repeats the SAME value (not just decreases) would also collide; the rebase must
        // trigger on `natural <= last` so a repeated-equal lap advances to the next number (3 → 4).
        LapSegmenter segmenter = new();
        TelemetryFrame[] frames =
        [
            Frame(lapNumber: 3, pos: 0.95f, ms: 0),
            Frame(lapNumber: 3, pos: 0.02f, ms: 10),   // crossing #1 — start observed, discarded
            Frame(lapNumber: 3, pos: 0.95f, ms: 20),
            Frame(lapNumber: 3, pos: 0.02f, ms: 30),   // crossing #2 — closes intrinsic lap 3 → assigned 3
            Frame(lapNumber: 3, pos: 0.95f, ms: 40),
            Frame(lapNumber: 3, pos: 0.02f, ms: 50),   // crossing #3 — closes repeated intrinsic lap 3 → assigned 4
            Frame(lapNumber: 3, pos: 0.50f, ms: 60),   // trailing partial — discarded
        ];

        List<CompletedLap> completed = [.. frames.Select(segmenter.Accept).Where(l => l is not null).Select(l => l!)];

        completed.Select(l => l.LapNumber).Should().Equal(3, 4);
    }

    [Fact]
    public void Strictly_increasing_counter_is_numbered_unchanged()
    {
        // Regression guard: on a normal session the sim counter never resets, so the relabel is a no-op
        // and the assigned number equals the intrinsic counter exactly (here, base 5 → 5, 6).
        LapSegmenter segmenter = new();
        TelemetryFrame[] frames =
        [
            Frame(lapNumber: 5, pos: 0.95f, ms: 0),
            Frame(lapNumber: 5, pos: 0.02f, ms: 10),   // crossing #1 — start observed, discarded
            Frame(lapNumber: 5, pos: 0.95f, ms: 20),
            Frame(lapNumber: 6, pos: 0.02f, ms: 30),   // crossing #2 — closes intrinsic lap 5 → assigned 5
            Frame(lapNumber: 6, pos: 0.95f, ms: 40),
            Frame(lapNumber: 7, pos: 0.02f, ms: 50),   // crossing #3 — closes intrinsic lap 6 → assigned 6
            Frame(lapNumber: 7, pos: 0.50f, ms: 60),   // trailing partial — discarded
        ];

        List<CompletedLap> completed = [.. frames.Select(segmenter.Accept).Where(l => l is not null).Select(l => l!)];

        completed.Select(l => l.LapNumber).Should().Equal(5, 6);
    }

    [Fact]
    public void Has_started_lap_is_false_until_the_first_crossing_then_stays_true()
    {
        // Out-lap frames (before the first start-line crossing) have no bounded lap; HasStartedLap reports
        // false there and flips true on the first crossing, so the compute emit-gate can drop pre-start
        // (out-lap) samples. The flag is only set on the crossing frame itself, not the frames before it.
        LapSegmenter segmenter = new();

        segmenter.Accept(Frame(lapNumber: 1, pos: 0.40f, ms: 0));
        segmenter.HasStartedLap.Should().BeFalse("no start-line crossing has been observed yet");

        segmenter.Accept(Frame(lapNumber: 1, pos: 0.95f, ms: 10));
        segmenter.HasStartedLap.Should().BeFalse();

        segmenter.Accept(Frame(lapNumber: 1, pos: 0.02f, ms: 20)); // first crossing (start observed)
        segmenter.HasStartedLap.Should().BeTrue("the first start-line crossing was just observed");

        segmenter.Accept(Frame(lapNumber: 1, pos: 0.50f, ms: 30));
        segmenter.HasStartedLap.Should().BeTrue("it stays latched for the rest of the stream");
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
