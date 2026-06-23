using FluentAssertions;
using SimCoach.Pipeline.Segmentation;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class TrackModelBuilderTests
{
    [Fact]
    public void Derives_one_corner_per_braking_zone_in_position_order()
    {
        CompletedLap lap = TestLaps.FastestClean(SyntheticTracks.Spa);

        TrackModel model = TrackModelBuilder.Build("spa", lap);

        // Spa's synthetic layout has three braking zones.
        model.Source.Should().Be(TrackModelSource.Derived);
        model.Corners.Should().HaveCount(3);
        model.Corners.Select(c => c.Id).Should().Equal("spa_t01", "spa_t02", "spa_t03");
        model.Corners.Should().OnlyContain(c => c.Name == null);
        model.Corners.Select(c => c.StartPosition).Should().BeInAscendingOrder();
        model.Corners.Should().OnlyContain(c => c.StartPosition < c.ApexPosition && c.ApexPosition < c.EndPosition);
        model.DerivedFromLapTimeMs.Should().Be(lap.LapTimeMs);
    }

    [Fact]
    public void Apex_positions_track_the_synthetic_corner_minima()
    {
        CompletedLap lap = TestLaps.FastestClean(SyntheticTracks.Spa);

        TrackModel model = TrackModelBuilder.Build("spa", lap);

        float[] expectedApexes = [.. SyntheticTracks.Spa.Corners.Select(c => c.ApexPos)];
        for (int i = 0; i < model.Corners.Count; i++)
        {
            model.Corners[i].ApexPosition.Should().BeApproximately(expectedApexes[i], 0.02f);
        }
    }

    [Fact]
    public void Build_is_deterministic()
    {
        CompletedLap lap = TestLaps.FastestClean(SyntheticTracks.Spa);

        TrackModel first = TrackModelBuilder.Build("spa", lap);
        TrackModel second = TrackModelBuilder.Build("spa", lap);

        second.Corners.Should().Equal(first.Corners);
    }

    [Fact]
    public void Derives_corners_for_an_uncovered_oval()
    {
        CompletedLap lap = TestLaps.FastestClean(SyntheticTracks.TestOval);

        TrackModel model = TrackModelBuilder.Build("test_oval", lap);

        model.Corners.Should().HaveCount(2);
        model.Corners.Select(c => c.Id).Should().Equal("test_oval_t01", "test_oval_t02");
    }
}
