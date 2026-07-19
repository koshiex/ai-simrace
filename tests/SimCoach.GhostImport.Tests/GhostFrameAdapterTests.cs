using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.GhostImport;
using Xunit;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// The ghost→frame adapter (B1a) must emit frames the median centerline builder will actually bin:
/// positive speed (past the teleport/stationary guard), mapped world position, and a monotonic
/// self-axis lap distance. A regression here re-introduces the silently-inert empty centerline.
/// </summary>
public sealed class GhostFrameAdapterTests
{
    private static IReadOnlyList<GhostRecord> SampleRecords() =>
    [
        new GhostRecord(0f, 1f, 0f, 0f, 0f, 1f, 0f),
        new GhostRecord(3f, 1f, 4f, 0.1f, 0f, 1f, 1f),   // +5 XZ arc from previous
        new GhostRecord(3f, 2f, 4f, 0.2f, 0.2f, 0.8f, 2f), // +0 XZ arc (pure vertical move)
        new GhostRecord(6f, 2f, 8f, 0.3f, 0.5f, 0.5f, 3f), // +5 XZ arc from previous
    ];

    [Fact]
    public void ToFrames_emits_one_frame_per_record()
    {
        IReadOnlyList<GhostRecord> records = SampleRecords();

        IReadOnlyList<TelemetryFrame> frames = GhostFrameAdapter.ToFrames(records);

        frames.Should().HaveCount(records.Count);
    }

    [Fact]
    public void ToFrames_stamps_positive_speed_on_every_frame()
    {
        IReadOnlyList<TelemetryFrame> frames = GhostFrameAdapter.ToFrames(SampleRecords());

        frames.Should().OnlyContain(f => f.SpeedMps > 0f);
    }

    [Fact]
    public void ToFrames_maps_world_position_xz_from_record()
    {
        IReadOnlyList<GhostRecord> records = SampleRecords();

        IReadOnlyList<TelemetryFrame> frames = GhostFrameAdapter.ToFrames(records);

        for (int i = 0; i < records.Count; i++)
        {
            frames[i].WorldPos.Should().NotBeNull();
            frames[i].WorldPos.X.Should().Be(records[i].WorldX);
            frames[i].WorldPos.Y.Should().Be(records[i].WorldY);
            frames[i].WorldPos.Z.Should().Be(records[i].WorldZ);
        }
    }

    [Fact]
    public void ToFrames_lap_distance_is_monotonic_non_decreasing_cumulative_xz_arc_length()
    {
        IReadOnlyList<TelemetryFrame> frames = GhostFrameAdapter.ToFrames(SampleRecords());

        frames[0].LapDistanceM.Should().Be(0f);
        for (int i = 1; i < frames.Count; i++)
        {
            frames[i].LapDistanceM.Should().BeGreaterThanOrEqualTo(frames[i - 1].LapDistanceM);
        }

        // XZ arc only — the pure-vertical move (index 2) adds nothing, the two 3-4-5 legs add 5 each.
        frames[1].LapDistanceM.Should().BeApproximately(5f, 1e-4f);
        frames[2].LapDistanceM.Should().BeApproximately(5f, 1e-4f);
        frames[3].LapDistanceM.Should().BeApproximately(10f, 1e-4f);
    }

    [Fact]
    public void ToFrames_leaves_gforce_null_so_lateral_g_reads_zero()
    {
        IReadOnlyList<TelemetryFrame> frames = GhostFrameAdapter.ToFrames(SampleRecords());

        frames.Should().OnlyContain(f => f.GForceG == null);
    }
}
