using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Pipeline.Tests.Segmentation;

public sealed class SectorSegmenterTests
{
    [Fact]
    public void Emits_a_split_per_sector_crossing_with_positive_times()
    {
        // Arrange — two Spa laps; sectors run 0→1→2 then wrap 2→0 at the line.
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 2);
        SectorSegmenter segmenter = new();

        // Act
        List<SectorSplit> splits = [];
        foreach (TelemetryFrame frame in frames)
        {
            if (segmenter.Accept(frame) is { } split)
            {
                splits.Add(split);
            }
        }

        // Assert — every crossing has a positive duration and reports the sector that just ended.
        splits.Should().NotBeEmpty();
        splits.Should().OnlyContain(s => s.SectorTimeMs > 0);
        splits.Select(s => s.SectorIndex).Should().Contain([0, 1, 2]);
    }
}
