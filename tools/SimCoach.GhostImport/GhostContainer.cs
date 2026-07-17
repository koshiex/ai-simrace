using System.Buffers.Binary;
using System.IO.Compression;

namespace SimCoach.GhostImport;

/// <summary>
/// Decodes the UE4 compressed-chunk container wrapping an ACC <c>.ghost</c>
/// (<c>docs/05-implementation/acc-ghost-format-re.md</c>): one or more chunks, each a <c>0x30</c>-byte
/// header followed by a zlib stream. Every chunk's inflate output is concatenated into the payload.
/// Fails loud (<see cref="InvalidDataException"/>) on a magic mismatch or a size that runs past the
/// file — a corrupt/foreign file must never decode to a plausible-looking path.
/// </summary>
internal static class GhostContainer
{
    /// <summary>u64 chunk magic (<c>c1 83 2a 9e …</c> little-endian).</summary>
    internal const ulong ChunkMagic = 0x9E2A83C1UL;

    private const int ChunkHeaderLength = 0x30;
    private const int CompressedSizeOffset = 0x20;
    private const int UncompressedSizeOffset = 0x28;

    internal static byte[] Inflate(ReadOnlySpan<byte> file)
    {
        using MemoryStream payload = new();
        int position = 0;
        while (position < file.Length)
        {
            if (position + ChunkHeaderLength > file.Length)
            {
                throw new InvalidDataException(
                    $"ghost container truncated: chunk header at offset {position} exceeds file length {file.Length}");
            }

            ReadOnlySpan<byte> header = file.Slice(position, ChunkHeaderLength);
            ulong magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
            if (magic != ChunkMagic)
            {
                throw new InvalidDataException(
                    $"ghost container magic mismatch at chunk offset {position}: "
                    + $"expected 0x{ChunkMagic:X}, got 0x{magic:X}");
            }

            long compressedSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(header[CompressedSizeOffset..]);
            long uncompressedSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(header[UncompressedSizeOffset..]);
            int streamStart = position + ChunkHeaderLength;
            if (compressedSize <= 0 || streamStart + compressedSize > file.Length)
            {
                throw new InvalidDataException(
                    $"ghost container chunk at offset {position} declares compressed size {compressedSize} "
                    + $"running past the file end {file.Length}");
            }

            InflateChunk(file.Slice(streamStart, (int)compressedSize), uncompressedSize, payload);
            position = streamStart + (int)compressedSize;
        }

        if (payload.Length == 0)
        {
            throw new InvalidDataException("ghost container produced an empty payload");
        }

        return payload.ToArray();
    }

    private static void InflateChunk(ReadOnlySpan<byte> compressed, long expectedUncompressed, Stream destination)
    {
        long before = destination.Length;
        using (MemoryStream source = new(compressed.ToArray(), writable: false))
        using (ZLibStream zlib = new(source, CompressionMode.Decompress))
        {
            zlib.CopyTo(destination);
        }

        long produced = destination.Length - before;
        if (expectedUncompressed > 0 && produced != expectedUncompressed)
        {
            throw new InvalidDataException(
                $"ghost chunk inflate produced {produced} bytes, header declared {expectedUncompressed}");
        }
    }
}
