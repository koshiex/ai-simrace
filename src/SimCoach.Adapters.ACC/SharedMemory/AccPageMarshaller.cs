using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SimCoach.Adapters.ACC.SharedMemory;

/// <summary>
/// Materializes ACC shared-memory page structs from raw page bytes.
/// Works on a copied buffer so callers can apply the packetId seqlock
/// (read id → copy page → re-read id via <see cref="ReadPacketId"/>) before paying the marshaling cost.
/// Intended buffer pattern: one reusable <c>byte[]</c> per page, sized at least the page's
/// <c>SizeBytes</c>, refilled each frame; <see cref="Read{T}"/> always parses from offset 0,
/// so arbitrary shared buffers with the page at a non-zero offset are not supported.
/// If per-frame marshaling allocations ever become a bottleneck, the escape hatch is blittable
/// mirror structs (<c>unsafe fixed</c> buffers + <c>MemoryMarshal.Read</c>) reusing the same golden layout tests.
/// </summary>
public static class AccPageMarshaller
{
    /// <summary>
    /// Reads a page struct from the start of <paramref name="pageBytes"/>.
    /// The buffer may be larger than the struct (memory-mapped views are page-granular).
    /// </summary>
    /// <exception cref="ArgumentNullException">The buffer is null.</exception>
    /// <exception cref="ArgumentException">The buffer is smaller than the marshaled struct size.</exception>
    public static T Read<T>(byte[] pageBytes) where T : struct
    {
        ArgumentNullException.ThrowIfNull(pageBytes);

        int requiredBytes = Marshal.SizeOf<T>();
        if (pageBytes.Length < requiredBytes)
        {
            throw new ArgumentException(
                $"Page buffer holds {pageBytes.Length} bytes but {typeof(T).Name} requires {requiredBytes}.",
                nameof(pageBytes));
        }

        var handle = GCHandle.Alloc(pageBytes, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Reads the seqlock <c>packetId</c> from page bytes without marshaling the whole page.
    /// Valid for the physics and graphics pages, whose native layouts both start with
    /// <c>int packetId</c>; the static page has no packetId.
    /// </summary>
    /// <exception cref="ArgumentException">The buffer is smaller than an int.</exception>
    public static int ReadPacketId(ReadOnlySpan<byte> pageBytes)
    {
        if (pageBytes.Length < sizeof(int))
        {
            throw new ArgumentException(
                $"Page buffer holds {pageBytes.Length} bytes but packetId requires {sizeof(int)}.",
                nameof(pageBytes));
        }

        return BinaryPrimitives.ReadInt32LittleEndian(pageBytes);
    }
}
