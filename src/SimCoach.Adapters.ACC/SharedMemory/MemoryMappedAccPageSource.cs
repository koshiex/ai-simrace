using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;

namespace SimCoach.Adapters.ACC.SharedMemory;

/// <summary>
/// Windows implementation of <see cref="IAccPageSource"/> over ACC's <c>Local\acpmf_*</c>
/// memory-mapped files. ACC creates the mappings on session load, so <see cref="TryConnect"/>
/// fails until the player enters a session. Once connected, a closed game is NOT detected as a
/// disconnect: our open handle keeps the named section alive, frames simply stop and resume when
/// ACC restarts and rewrites the same section. Stale-packetId disconnect detection is deferred.
/// Thin adapter by design — all poll/seqlock/reconnect logic lives in
/// <see cref="AccFrameAcquisition"/> and is covered by cross-platform tests;
/// this class is verified manually on a Windows machine with ACC running (plan B7).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MemoryMappedAccPageSource : IAccPageSource
{
    private const string PhysicsMapName = @"Local\acpmf_physics";
    private const string GraphicsMapName = @"Local\acpmf_graphics";
    private const string StaticMapName = @"Local\acpmf_static";

    private MemoryMappedFile? _physicsFile;
    private MemoryMappedFile? _graphicsFile;
    private MemoryMappedFile? _staticFile;
    private MemoryMappedViewAccessor? _physicsView;
    private MemoryMappedViewAccessor? _graphicsView;
    private MemoryMappedViewAccessor? _staticView;

    public bool TryConnect()
    {
        if (_physicsView is not null)
        {
            return true;
        }

        try
        {
            _physicsFile = MemoryMappedFile.OpenExisting(PhysicsMapName, MemoryMappedFileRights.Read);
            _graphicsFile = MemoryMappedFile.OpenExisting(GraphicsMapName, MemoryMappedFileRights.Read);
            _staticFile = MemoryMappedFile.OpenExisting(StaticMapName, MemoryMappedFileRights.Read);
            _physicsView = _physicsFile.CreateViewAccessor(0, AccPhysicsPage.SizeBytes, MemoryMappedFileAccess.Read);
            _graphicsView = _graphicsFile.CreateViewAccessor(0, AccGraphicsPage.SizeBytes, MemoryMappedFileAccess.Read);
            _staticView = _staticFile.CreateViewAccessor(0, AccStaticPage.SizeBytes, MemoryMappedFileAccess.Read);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            Close();
            return false;
        }
        catch
        {
            // Unexpected failure mid-connect: release the partially opened handles so the next
            // TryConnect does not early-return true against half-initialized state.
            Close();
            throw;
        }
    }

    public bool TryReadPacketId(AccPage page, out int packetId)
    {
        packetId = 0;
        MemoryMappedViewAccessor? view = page switch
        {
            AccPage.Physics => _physicsView,
            AccPage.Graphics => _graphicsView,
            AccPage.Static => throw new ArgumentOutOfRangeException(
                nameof(page), page, "The static page has no packetId."),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown ACC page."),
        };

        if (view is null)
        {
            return false;
        }

        packetId = view.ReadInt32(0);
        return true;
    }

    public bool TryCopyPage(AccPage page, byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        (MemoryMappedViewAccessor? view, int sizeBytes) = page switch
        {
            AccPage.Physics => (_physicsView, AccPhysicsPage.SizeBytes),
            AccPage.Graphics => (_graphicsView, AccGraphicsPage.SizeBytes),
            AccPage.Static => (_staticView, AccStaticPage.SizeBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, "Unknown ACC page."),
        };

        if (view is null)
        {
            return false;
        }

        if (destination.Length < sizeBytes)
        {
            throw new ArgumentException(
                $"Destination holds {destination.Length} bytes but the {page} page requires {sizeBytes}.",
                nameof(destination));
        }

        view.ReadArray(0, destination, 0, sizeBytes);
        return true;
    }

    public void Dispose() => Close();

    private void Close()
    {
        _physicsView?.Dispose();
        _graphicsView?.Dispose();
        _staticView?.Dispose();
        _physicsFile?.Dispose();
        _graphicsFile?.Dispose();
        _staticFile?.Dispose();
        _physicsView = null;
        _graphicsView = null;
        _staticView = null;
        _physicsFile = null;
        _graphicsFile = null;
        _staticFile = null;
    }
}
