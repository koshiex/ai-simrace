using FluentAssertions;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.TestKit.Tests;

/// <summary>
/// Smoke tests for the synthetic telemetry fixture: lap/sector structure, position wrap, populated
/// world coordinates, and dirty-lap validity. Runs against both a dataset-covered track (Spa) and an
/// uncovered one (TestOval).
/// </summary>
public sealed class SyntheticSessionBuilderTests
{
    public static TheoryData<SyntheticTrack> Tracks() => [SyntheticTracks.Spa, SyntheticTracks.TestOval];

    [Theory]
    [MemberData(nameof(Tracks))]
    public void Builds_expected_frame_count_and_lap_sequence(SyntheticTrack track)
    {
        // Arrange
        const int lapCount = 3;
        const int samplesPerLap = 200;

        // Act
        IReadOnlyList<TelemetryFrame> frames =
            SyntheticSessionBuilder.Build(track, lapCount, samplesPerLap: samplesPerLap);

        // Assert
        frames.Should().HaveCount(lapCount * samplesPerLap);
        frames.Select(f => f.LapNumber).Distinct().Should().Equal(1, 2, 3);
        frames.Select(f => f.T.ToDateTimeOffset()).Should().BeInAscendingOrder();
    }

    [Theory]
    [MemberData(nameof(Tracks))]
    public void Each_lap_covers_all_sectors_monotonically_and_wraps_position(SyntheticTrack track)
    {
        // Arrange / Act
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(track, lapCount: 2);

        // Assert
        foreach (IGrouping<int, TelemetryFrame> lap in frames.GroupBy(f => f.LapNumber))
        {
            TelemetryFrame[] lapFrames = [.. lap];
            int[] sectors = [.. lapFrames.Select(f => f.CurrentSectorIndex)];

            sectors.Distinct().Order().Should().Equal(Enumerable.Range(0, track.SectorCount));
            sectors.Should().BeInAscendingOrder(); // sector never decreases within a lap
            lapFrames.Select(f => f.NormalizedCarPosition).Should().BeInAscendingOrder();
            lapFrames[0].NormalizedCarPosition.Should().Be(0f); // position wraps to 0 each lap
            lapFrames.Should().OnlyContain(f => f.SectorCount == track.SectorCount);
        }
    }

    [Theory]
    [MemberData(nameof(Tracks))]
    public void World_pos_magnitude_is_non_zero(SyntheticTrack track)
    {
        // Arrange / Act
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(track, lapCount: 1);

        // Assert — Y is identically 0 and Z is 0 at lap start, so assert the X/Z magnitude, not axes.
        frames.Should().OnlyContain(f =>
            (f.WorldPos.X * f.WorldPos.X) + (f.WorldPos.Z * f.WorldPos.Z) > 1f);
    }

    [Fact]
    public void Injected_dirty_lap_marks_only_that_laps_frames_invalid()
    {
        // Arrange
        HashSet<int> dirty = [2];

        // Act
        IReadOnlyList<TelemetryFrame> frames =
            SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 3, dirtyLaps: dirty);

        // Assert
        frames.Where(f => f.LapNumber == 2).Should().OnlyContain(f => !f.IsValidLap);
        frames.Where(f => f.LapNumber != 2).Should().OnlyContain(f => f.IsValidLap);
    }
}
