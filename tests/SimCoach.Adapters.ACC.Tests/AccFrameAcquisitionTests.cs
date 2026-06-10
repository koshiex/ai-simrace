using FluentAssertions;
using SimCoach.Adapters.ACC.SharedMemory;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Tests for the seqlock/dedup/page-caching core that turns raw shared-memory pages into
/// coherent snapshots. Timing is controlled via <see cref="FakeClock"/>; no threads involved.
/// </summary>
public sealed class AccFrameAcquisitionTests
{
    private static readonly AccReaderOptions _options = new()
    {
        StaticRefreshInterval = TimeSpan.FromSeconds(1),
        MaxSeqlockRetries = 4,
    };

    private readonly FakeAccPageSource _source = new();
    private readonly FakeClock _clock = new();

    private AccFrameAcquisition CreateAcquisition() => new(_source, _clock, _options);

    [Fact]
    public void First_new_packet_yields_snapshot_with_marshaled_pages()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 10, gear: 4));
        _source.SetGraphicsPage(GraphicsPageBytes(packetId: 100, completedLaps: 5));
        _source.SetStaticPage(StaticPageBytes(maxRpm: 8650));
        AccFrameAcquisition acquisition = CreateAcquisition();

        // Act
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert
        status.Should().Be(AccAcquisitionStatus.NewFrame);
        snapshot.Should().NotBeNull();
        snapshot!.Physics.PacketId.Should().Be(10);
        snapshot.Physics.Gear.Should().Be(4);
        snapshot.Graphics.CompletedLaps.Should().Be(5);
        snapshot.Static.MaxRpm.Should().Be(8650);
        snapshot.CapturedAt.Should().Be(_clock.GetUtcNow());
        snapshot.CapturedAtTimestamp.Should().Be(_clock.GetTimestamp());
    }

    [Fact]
    public void Unchanged_packetId_yields_no_new_frame()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 10));
        AccFrameAcquisition acquisition = CreateAcquisition();
        acquisition.TryAcquire(out _);

        // Act
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert
        status.Should().Be(AccAcquisitionStatus.NoNewFrame);
        snapshot.Should().BeNull();
        _source.PhysicsCopyCount.Should().Be(1, "an unchanged packetId must not trigger a page copy");
    }

    [Fact]
    public void Incremented_packetId_yields_next_snapshot()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 10));
        AccFrameAcquisition acquisition = CreateAcquisition();
        acquisition.TryAcquire(out _);
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 11, gear: 5));

        // Act
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert
        status.Should().Be(AccAcquisitionStatus.NewFrame);
        snapshot!.Physics.PacketId.Should().Be(11);
        snapshot.Physics.Gear.Should().Be(5);
    }

    [Fact]
    public void Torn_read_retries_until_packetId_is_stable()
    {
        // Arrange — id changes during the first copy (7 → 8), then stabilizes at 8
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 8, gear: 3));
        _source.ScriptedPhysicsPacketIds.Enqueue(7);
        _source.ScriptedPhysicsPacketIds.Enqueue(8);
        _source.ScriptedPhysicsPacketIds.Enqueue(8);
        AccFrameAcquisition acquisition = CreateAcquisition();

        // Act
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert
        status.Should().Be(AccAcquisitionStatus.NewFrame);
        snapshot!.Physics.PacketId.Should().Be(8);
        _source.PhysicsCopyCount.Should().Be(2, "the torn first copy must be retried exactly once");
    }

    [Fact]
    public void Continuously_torn_reads_skip_the_frame_after_max_retries()
    {
        // Arrange — packetId changes on every read; seqlock can never validate a copy
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 99));
        foreach (int packetId in new[] { 1, 2, 3, 4, 5 })
        {
            _source.ScriptedPhysicsPacketIds.Enqueue(packetId);
        }

        AccFrameAcquisition acquisition = CreateAcquisition();

        // Act
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert
        status.Should().Be(AccAcquisitionStatus.NoNewFrame);
        snapshot.Should().BeNull();
        _source.PhysicsCopyCount.Should().Be(_options.MaxSeqlockRetries);
    }

    [Fact]
    public void Graphics_page_is_remarshaled_only_when_its_packetId_changes()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        _source.SetGraphicsPage(GraphicsPageBytes(packetId: 100, completedLaps: 5));
        AccFrameAcquisition acquisition = CreateAcquisition();
        acquisition.TryAcquire(out _);

        // graphics content changes WITHOUT a packetId bump — must keep serving the cached copy
        _source.SetGraphicsPage(GraphicsPageBytes(packetId: 100, completedLaps: 6));
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 2));

        // Act
        acquisition.TryAcquire(out AccTelemetrySnapshot? cachedSnapshot);

        _source.SetGraphicsPage(GraphicsPageBytes(packetId: 101, completedLaps: 7));
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 3));
        acquisition.TryAcquire(out AccTelemetrySnapshot? refreshedSnapshot);

        // Assert
        cachedSnapshot!.Graphics.CompletedLaps.Should().Be(
            5, "graphics must be cached while its packetId is unchanged");
        refreshedSnapshot!.Graphics.CompletedLaps.Should().Be(7);
        _source.GraphicsCopyCount.Should().Be(2);
    }

    [Fact]
    public void Continuously_torn_graphics_keeps_the_cached_page()
    {
        // Arrange — first acquire caches graphics (id 100, laps 5)
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        _source.SetGraphicsPage(GraphicsPageBytes(packetId: 100, completedLaps: 5));
        AccFrameAcquisition acquisition = CreateAcquisition();
        acquisition.TryAcquire(out _);

        // graphics now torn on every read: 1 dedup-check read + 4 post-copy reads, all different
        _source.SetGraphicsPage(GraphicsPageBytes(packetId: 105, completedLaps: 9));
        foreach (int packetId in new[] { 101, 102, 103, 104, 105 })
        {
            _source.ScriptedGraphicsPacketIds.Enqueue(packetId);
        }

        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 2));

        // Act
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert — the physics frame still goes out, with the last coherent graphics page
        status.Should().Be(AccAcquisitionStatus.NewFrame);
        snapshot!.Graphics.CompletedLaps.Should().Be(5);
        _source.GraphicsCopyCount.Should().Be(1 + _options.MaxSeqlockRetries);
    }

    [Fact]
    public void Continuously_torn_graphics_without_cache_accepts_the_torn_page()
    {
        // Arrange — no cached graphics exists; a possibly-torn page beats a default struct
        // with null arrays downstream
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        _source.SetGraphicsPage(GraphicsPageBytes(packetId: 105, completedLaps: 7));
        foreach (int packetId in new[] { 101, 102, 103, 104, 105 })
        {
            _source.ScriptedGraphicsPacketIds.Enqueue(packetId);
        }

        AccFrameAcquisition acquisition = CreateAcquisition();

        // Act
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert
        status.Should().Be(AccAcquisitionStatus.NewFrame);
        snapshot!.Graphics.CompletedLaps.Should().Be(7);
        snapshot.Graphics.CarCoordinates.Should().NotBeNull();
    }

    [Fact]
    public void Static_page_is_refreshed_only_after_the_configured_interval()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        _source.SetStaticPage(StaticPageBytes(maxRpm: 8650));
        AccFrameAcquisition acquisition = CreateAcquisition();
        acquisition.TryAcquire(out _);

        _source.SetStaticPage(StaticPageBytes(maxRpm: 9250));
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 2));
        _clock.Advance(TimeSpan.FromMilliseconds(500));

        // Act — inside the interval: cached static
        acquisition.TryAcquire(out AccTelemetrySnapshot? cachedSnapshot);

        _clock.Advance(TimeSpan.FromMilliseconds(600));
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 3));
        acquisition.TryAcquire(out AccTelemetrySnapshot? refreshedSnapshot);

        // Assert
        cachedSnapshot!.Static.MaxRpm.Should().Be(8650);
        refreshedSnapshot!.Static.MaxRpm.Should().Be(9250);
        _source.StaticCopyCount.Should().Be(2);
    }

    [Fact]
    public void Disconnected_source_reports_disconnected()
    {
        // Arrange
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 1));
        AccFrameAcquisition acquisition = CreateAcquisition();
        _source.IsDisconnected = true;

        // Act
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert
        status.Should().Be(AccAcquisitionStatus.Disconnected);
        snapshot.Should().BeNull();
    }

    [Fact]
    public void Reset_forgets_seen_packets_and_rereads_all_pages()
    {
        // Arrange — after a reconnect the same packetId must be treated as a fresh frame
        _source.SetPhysicsPage(PhysicsPageBytes(packetId: 10));
        AccFrameAcquisition acquisition = CreateAcquisition();
        acquisition.TryAcquire(out _);

        // Act
        acquisition.Reset();
        AccAcquisitionStatus status = acquisition.TryAcquire(out AccTelemetrySnapshot? snapshot);

        // Assert
        status.Should().Be(AccAcquisitionStatus.NewFrame);
        snapshot!.Physics.PacketId.Should().Be(10);
        _source.StaticCopyCount.Should().Be(2, "a reset must force the static page to be re-read");
    }

    private static byte[] PhysicsPageBytes(int packetId, int gear = 3) =>
        new PageFixtureBuilder(AccPhysicsPage.SizeBytes)
            .WithInt32(0, packetId)
            .WithInt32(16, gear)
            .Build();

    private static byte[] GraphicsPageBytes(int packetId, int completedLaps) =>
        new PageFixtureBuilder(AccGraphicsPage.SizeBytes)
            .WithInt32(0, packetId)
            .WithInt32(132, completedLaps)
            .Build();

    private static byte[] StaticPageBytes(int maxRpm) =>
        new PageFixtureBuilder(AccStaticPage.SizeBytes)
            .WithInt32(412, maxRpm)
            .Build();
}
