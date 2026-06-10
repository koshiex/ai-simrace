using SimCoach.Adapters.ACC.SharedMemory;

namespace SimCoach.Adapters.ACC;

/// <summary>
/// Single-threaded acquisition core: detects new physics packets, guards page copies with the
/// packetId seqlock (read id → copy → re-read id, retry on mismatch), and caches the slower
/// pages (graphics per its own packetId, static per refresh interval). Owns reusable buffers —
/// call only from one thread at a time.
/// </summary>
public sealed class AccFrameAcquisition
{
    private readonly IAccPageSource _pageSource;
    private readonly TimeProvider _timeProvider;
    private readonly AccReaderOptions _options;

    private readonly byte[] _physicsBuffer = new byte[AccPhysicsPage.SizeBytes];
    private readonly byte[] _graphicsBuffer = new byte[AccGraphicsPage.SizeBytes];
    private readonly byte[] _staticBuffer = new byte[AccStaticPage.SizeBytes];

    private bool _hasSeenPhysics;
    private int _lastPhysicsPacketId;
    private bool _hasGraphics;
    private int _lastGraphicsPacketId;
    private AccGraphicsPage _graphics;
    private bool _hasStatic;
    private long _staticRefreshedAtTimestamp;
    private AccStaticPage _static;

    public AccFrameAcquisition(IAccPageSource pageSource, TimeProvider timeProvider, AccReaderOptions options)
    {
        ArgumentNullException.ThrowIfNull(pageSource);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();
        _pageSource = pageSource;
        _timeProvider = timeProvider;
        _options = options;
    }

    /// <summary>Forgets all seen packets and cached pages; call after a reconnect.</summary>
    public void Reset()
    {
        _hasSeenPhysics = false;
        _hasGraphics = false;
        _hasStatic = false;
    }

    /// <summary>One poll tick: produces a snapshot when a new coherent physics packet is available.</summary>
    public AccAcquisitionStatus TryAcquire(out AccTelemetrySnapshot? snapshot)
    {
        snapshot = null;

        if (!_pageSource.TryReadPacketId(AccPage.Physics, out int physicsPacketId))
        {
            return AccAcquisitionStatus.Disconnected;
        }

        if (_hasSeenPhysics && physicsPacketId == _lastPhysicsPacketId)
        {
            return AccAcquisitionStatus.NoNewFrame;
        }

        if (!TryCopyWithSeqlock(AccPage.Physics, _physicsBuffer, ref physicsPacketId, out bool isStable))
        {
            return AccAcquisitionStatus.Disconnected;
        }

        if (!isStable)
        {
            return AccAcquisitionStatus.NoNewFrame; // torn beyond the retry budget — skip this tick
        }

        AccPhysicsPage physics = AccPageMarshaller.Read<AccPhysicsPage>(_physicsBuffer);
        _lastPhysicsPacketId = physicsPacketId;
        _hasSeenPhysics = true;

        if (!TryRefreshGraphics() || !TryRefreshStatic())
        {
            return AccAcquisitionStatus.Disconnected;
        }

        snapshot = new AccTelemetrySnapshot(
            _timeProvider.GetUtcNow(), _timeProvider.GetTimestamp(), physics, _graphics, _static);
        return AccAcquisitionStatus.NewFrame;
    }

    /// <summary>
    /// Seqlock copy: the page is coherent only if its packetId is identical before and after
    /// the copy. On mismatch retries with the newer id. Returns false on disconnect;
    /// <paramref name="isStable"/> is false when the retry budget ran out.
    /// </summary>
    private bool TryCopyWithSeqlock(AccPage page, byte[] buffer, ref int packetId, out bool isStable)
    {
        isStable = false;
        for (int attempt = 0; attempt < _options.MaxSeqlockRetries; attempt++)
        {
            if (!_pageSource.TryCopyPage(page, buffer))
            {
                return false;
            }

            if (!_pageSource.TryReadPacketId(page, out int packetIdAfterCopy))
            {
                return false;
            }

            if (packetIdAfterCopy == packetId)
            {
                isStable = true;
                return true;
            }

            packetId = packetIdAfterCopy;
        }

        return true;
    }

    private bool TryRefreshGraphics()
    {
        if (!_pageSource.TryReadPacketId(AccPage.Graphics, out int graphicsPacketId))
        {
            return false;
        }

        if (_hasGraphics && graphicsPacketId == _lastGraphicsPacketId)
        {
            return true;
        }

        if (!TryCopyWithSeqlock(AccPage.Graphics, _graphicsBuffer, ref graphicsPacketId, out bool isStable))
        {
            return false;
        }

        // An unstable copy keeps the cached page — unless there is none yet, in which case a
        // possibly-torn graphics page beats a default struct with null arrays downstream.
        if (isStable || !_hasGraphics)
        {
            _graphics = AccPageMarshaller.Read<AccGraphicsPage>(_graphicsBuffer);
            _lastGraphicsPacketId = graphicsPacketId;
            _hasGraphics = true;
        }

        return true;
    }

    private bool TryRefreshStatic()
    {
        // Monotonic time: a wall-clock NTP step must not stall or double-trigger the refresh.
        if (_hasStatic && _timeProvider.GetElapsedTime(_staticRefreshedAtTimestamp) < _options.StaticRefreshInterval)
        {
            return true;
        }

        if (!_pageSource.TryCopyPage(AccPage.Static, _staticBuffer))
        {
            return false;
        }

        _static = AccPageMarshaller.Read<AccStaticPage>(_staticBuffer);
        _staticRefreshedAtTimestamp = _timeProvider.GetTimestamp();
        _hasStatic = true;
        return true;
    }
}
