namespace SimCoach.Adapters.ACC.SharedMemory;

/// <summary>
/// Seam over ACC's shared-memory pages so the poll/seqlock/reconnect logic is unit-testable
/// off-Windows. The real implementation wraps memory-mapped files; tests use a scriptable fake.
/// </summary>
public interface IAccPageSource : IDisposable
{
    /// <summary>
    /// Opens the shared-memory pages. Returns false while ACC is not running or has not
    /// loaded a session yet (the mappings are created on session load). Safe to call repeatedly.
    /// </summary>
    bool TryConnect();

    /// <summary>
    /// Reads the live seqlock packetId of the physics or graphics page.
    /// Returns false when the source is disconnected.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The static page has no packetId.</exception>
    bool TryReadPacketId(AccPage page, out int packetId);

    /// <summary>
    /// Copies the page's current bytes into <paramref name="destination"/> (sized at least the
    /// page's <c>SizeBytes</c>). Returns false when the source is disconnected.
    /// </summary>
    bool TryCopyPage(AccPage page, byte[] destination);
}
