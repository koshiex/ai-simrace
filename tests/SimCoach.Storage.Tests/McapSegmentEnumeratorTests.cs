using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Segmentation;
using SimCoach.Storage.Mcap;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Storage.Tests;

public sealed class McapSegmentEnumeratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "simcoach-segments-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Reads_every_frame_in_order_across_segments()
    {
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        // 200 samples/lap; 150 per segment forces segment boundaries that land inside a lap.
        SegmentFixture.Write(_dir, frames, framesPerSegment: 150);

        List<TelemetryFrame> read = [.. McapSegmentEnumerator.Read(_dir)];

        read.Should().HaveCount(frames.Count);
        read.Select(f => f.NormalizedCarPosition).Should().Equal(frames.Select(f => f.NormalizedCarPosition));
        read.Select(f => f.LapNumber).Should().Equal(frames.Select(f => f.LapNumber));
    }

    [Fact]
    public void A_lap_spans_a_segment_boundary()
    {
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        SegmentFixture.Write(_dir, frames, framesPerSegment: 150);

        // Re-segmenting the stitched stream must recover the interior laps whole — only possible if the
        // enumerator joined segments across the mid-lap boundaries.
        LapSegmenter segmenter = new();
        List<CompletedLap> laps = [];
        foreach (TelemetryFrame frame in McapSegmentEnumerator.Read(_dir))
        {
            CompletedLap? completed = segmenter.Accept(frame);
            if (completed is not null)
            {
                laps.Add(completed);
            }
        }

        laps.Select(l => l.LapNumber).Should().Equal(2, 3);
        laps.Should().OnlyContain(l => l.Frames.Count == 200);
    }

    [Fact]
    public void Resolve_segment_paths_accepts_a_single_file()
    {
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        SegmentFixture.Write(_dir, frames, framesPerSegment: 150);
        string firstSegment = Directory.GetFiles(_dir, "*.mcap").OrderBy(p => p, StringComparer.Ordinal).First();

        IReadOnlyList<string> resolved = McapSegmentEnumerator.ResolveSegmentPaths(firstSegment);

        resolved.Should().ContainSingle().Which.Should().Be(firstSegment);
    }

    [Fact]
    public void Empty_directory_throws()
    {
        Directory.CreateDirectory(_dir);

        Action read = () => _ = McapSegmentEnumerator.Read(_dir).ToList();

        read.Should().Throw<FileNotFoundException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
