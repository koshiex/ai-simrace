using SimCoach.Adapters.ACC.SharedMemory;

namespace SimCoach.Adapters.ACC.Tests;

/// <summary>
/// Scriptable in-memory <see cref="IAccPageSource"/> for testing the poll/seqlock/reconnect
/// logic without Windows shared memory. PacketId reads fall back to the page buffer unless
/// a scripted queue is supplied (used to simulate torn reads).
/// </summary>
internal sealed class FakeAccPageSource : IAccPageSource
{
    // volatile: tests mutate these from the consumer thread while the poll thread reads them
    private volatile byte[] _physicsPage = new byte[AccPhysicsPage.SizeBytes];
    private volatile byte[] _graphicsPage = new byte[AccGraphicsPage.SizeBytes];
    private volatile byte[] _staticPage = new byte[AccStaticPage.SizeBytes];
    private volatile bool _isDisconnected;

    /// <summary>Scripted TryConnect results; empty queue means "always succeed".</summary>
    public Queue<bool> ConnectResults { get; } = new();

    /// <summary>Scripted physics packetId reads; empty queue means "read from the page buffer".</summary>
    public Queue<int> ScriptedPhysicsPacketIds { get; } = new();

    /// <summary>Scripted graphics packetId reads; empty queue means "read from the page buffer".</summary>
    public Queue<int> ScriptedGraphicsPacketIds { get; } = new();

    public int ConnectCallCount { get; private set; }

    public int PhysicsCopyCount { get; private set; }

    public int GraphicsCopyCount { get; private set; }

    public int StaticCopyCount { get; private set; }

    /// <summary>When true, all reads fail as if the game exited. Cleared by a successful TryConnect.</summary>
    public bool IsDisconnected
    {
        get => _isDisconnected;
        set => _isDisconnected = value;
    }

    public void SetPhysicsPage(byte[] page) => _physicsPage = page;

    public void SetGraphicsPage(byte[] page) => _graphicsPage = page;

    public void SetStaticPage(byte[] page) => _staticPage = page;

    public bool TryConnect()
    {
        ConnectCallCount++;
        bool isConnected = ConnectResults.Count == 0 || ConnectResults.Dequeue();
        if (isConnected)
        {
            _isDisconnected = false;
        }

        return isConnected;
    }

    public bool TryReadPacketId(AccPage page, out int packetId)
    {
        packetId = 0;
        if (_isDisconnected)
        {
            return false;
        }

        Queue<int>? scripted = page switch
        {
            AccPage.Physics => ScriptedPhysicsPacketIds,
            AccPage.Graphics => ScriptedGraphicsPacketIds,
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, "The static page has no packetId."),
        };

        if (scripted.Count > 0)
        {
            packetId = scripted.Dequeue();
            return true;
        }

        packetId = AccPageMarshaller.ReadPacketId(BufferFor(page));
        return true;
    }

    public bool TryCopyPage(AccPage page, byte[] destination)
    {
        if (_isDisconnected)
        {
            return false;
        }

        byte[] source = BufferFor(page);
        source.AsSpan().CopyTo(destination);
        switch (page)
        {
            case AccPage.Physics:
                PhysicsCopyCount++;
                break;
            case AccPage.Graphics:
                GraphicsCopyCount++;
                break;
            case AccPage.Static:
                StaticCopyCount++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(page));
        }

        return true;
    }

    public void Dispose()
    {
        // Nothing to release; the fake exists only to script page reads.
    }

    private byte[] BufferFor(AccPage page) => page switch
    {
        AccPage.Physics => _physicsPage,
        AccPage.Graphics => _graphicsPage,
        AccPage.Static => _staticPage,
        _ => throw new ArgumentOutOfRangeException(nameof(page)),
    };
}
