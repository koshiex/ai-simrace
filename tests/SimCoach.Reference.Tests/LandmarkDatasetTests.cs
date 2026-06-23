using System.Text;
using FluentAssertions;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class LandmarkDatasetTests
{
    [Fact]
    public void Covered_track_resolves_named_corners_with_sane_normalized_ranges()
    {
        var dataset = LandmarkDataset.Load();

        bool resolved = dataset.TryGetCorners("spa", SyntheticTracks.Spa.LapLengthM, out IReadOnlyList<Corner> corners);

        resolved.Should().BeTrue();
        corners.Should().NotBeEmpty();
        corners.Should().OnlyContain(c => c.Name != null && c.Id.StartsWith("spa_", StringComparison.Ordinal));
        corners.Should().OnlyContain(c =>
            c.StartPosition >= 0f && c.StartPosition < c.EndPosition && c.EndPosition <= 1f
            && c.ApexPosition >= c.StartPosition && c.ApexPosition <= c.EndPosition);
    }

    [Fact]
    public void Covered_track_corners_are_sorted_by_position()
    {
        var dataset = LandmarkDataset.Load();

        dataset.TryGetCorners("spa", SyntheticTracks.Spa.LapLengthM, out IReadOnlyList<Corner> corners);

        corners.Select(c => c.StartPosition).Should().BeInAscendingOrder();
    }

    [Fact]
    public void Uncovered_track_returns_false()
    {
        var dataset = LandmarkDataset.Load();

        bool resolved = dataset.TryGetCorners("test_oval", 2000f, out IReadOnlyList<Corner> corners);

        resolved.Should().BeFalse();
        corners.Should().BeEmpty();
    }

    [Fact]
    public void Out_of_range_landmark_drops_the_whole_track_to_derive()
    {
        // distanceRoundLapEnd (9999) exceeds the lap length (2000) → the track must drop to the fallback.
        const string json =
            """
            { "TrackLandmarksData": [ {
                "accTrackName": "Test:track config",
                "trackLandmarks": [
                    { "landmarkName": "ok", "distanceRoundLapStart": 100, "distanceRoundLapEnd": 200 },
                    { "landmarkName": "bad", "distanceRoundLapStart": 500, "distanceRoundLapEnd": 9999 }
                ] } ] }
            """;
        LandmarkDataset dataset = LoadJson(json);

        bool resolved = dataset.TryGetCorners("test", 2000f, out IReadOnlyList<Corner> corners);

        resolved.Should().BeFalse();
        corners.Should().BeEmpty();
    }

    [Fact]
    public void Custom_in_range_landmarks_resolve()
    {
        const string json =
            """
            { "TrackLandmarksData": [ {
                "accTrackName": "Test:track config",
                "trackLandmarks": [
                    { "landmarkName": "turn_one", "distanceRoundLapStart": 200, "distanceRoundLapEnd": 400 }
                ] } ] }
            """;
        LandmarkDataset dataset = LoadJson(json);

        bool resolved = dataset.TryGetCorners("test", 2000f, out IReadOnlyList<Corner> corners);

        resolved.Should().BeTrue();
        corners.Should().ContainSingle();
        corners[0].Id.Should().Be("test_turn_one");
        corners[0].StartPosition.Should().BeApproximately(0.1f, 1e-4f);
        corners[0].EndPosition.Should().BeApproximately(0.2f, 1e-4f);
    }

    private static LandmarkDataset LoadJson(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return LandmarkDataset.LoadFrom(stream);
    }
}
