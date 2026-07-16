using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SimCoach.GhostImport;

namespace SimCoach.GhostImport.Tests;

/// <summary>
/// Builds an in-code synthetic byte stream to the <c>docs/05-implementation/acc-ghost-format-re.md</c>
/// spec (container + payload + 130-byte records).
///
/// <para><b>M8 — this is a SELF-CONSISTENCY guard, NOT a format-correctness proof.</b> The bytes are
/// encoded by the SAME reading of the format doc that the decoder inverts, so these tests prove only that
/// the decoder inverts the encoder (a regression/refactor guard). A shared misread of the real ACC field
/// offsets or stride would green every test here while shipping a wrong world path. Format correctness is
/// established out-of-band by the import-time bbox + arithmetic guards firing loudly and by the required
/// per-car/track manual validation against a REAL accreplay <c>.ghost</c> (OD5) — NEVER a committed
/// <c>.ghost</c>. This fixture never touches the network and never reads a real specimen.</para>
/// </summary>
internal static class SyntheticGhostFixture
{
    private const uint PayloadVersion = 4;
    private const uint UnknownLapTimeField = 106_037; // shape-only; the decoder does not interpret it.
    private const uint PhysicsPageSizeField = 800;
    private const uint TrailerLeadValue = 3;
    private const int TrailerLength = 11;
    private const int RecordStride = 130;
    private const int ChunkHeaderLength = 0x30;
    private const ulong ContainerMagic = 0x9E2A83C1UL;
    private const ulong BlockSize = 0x20000UL;

    internal static byte[] BuildGhost(string trackId, IReadOnlyList<GhostRecord> records, int chunkCount = 2) =>
        BuildContainer(BuildPayload(trackId, records), chunkCount);

    internal static byte[] BuildPayload(string trackId, IReadOnlyList<GhostRecord> records)
    {
        byte[] trackBytes = Encoding.ASCII.GetBytes(trackId);
        int stringLength = trackBytes.Length + 1; // includes the trailing NUL
        int recordStart = 21 + stringLength + sizeof(uint);
        int payloadLength = recordStart + (records.Count * RecordStride) + TrailerLength;

        byte[] payload = new byte[payloadLength];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0), (uint)(payloadLength - sizeof(uint)));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), PayloadVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), UnknownLapTimeField);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), PhysicsPageSizeField);
        payload[16] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(17), (uint)stringLength);
        trackBytes.CopyTo(payload.AsSpan(21));
        payload[21 + trackBytes.Length] = 0; // NUL terminator
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(21 + stringLength), (uint)records.Count);

        for (int i = 0; i < records.Count; i++)
        {
            WriteRecord(payload.AsSpan(recordStart + (i * RecordStride), RecordStride), records[i]);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(payloadLength - TrailerLength), TrailerLeadValue);
        return payload;
    }

    private static void WriteRecord(Span<byte> record, GhostRecord value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(record[0..], value.WorldX);
        BinaryPrimitives.WriteSingleLittleEndian(record[4..], value.WorldY);
        BinaryPrimitives.WriteSingleLittleEndian(record[8..], value.WorldZ);
        BinaryPrimitives.WriteSingleLittleEndian(record[12..], value.Yaw);
        record[24] = (byte)Math.Round(value.BrakeNorm * 255f);
        record[25] = (byte)Math.Round(value.ThrottleNorm * 255f);
        BinaryPrimitives.WriteSingleLittleEndian(record[126..], value.RawTimestamp);
    }

    internal static byte[] BuildContainer(byte[] payload, int chunkCount)
    {
        int perChunk = (payload.Length + chunkCount - 1) / chunkCount;
        using var file = new MemoryStream();
        for (int offset = 0; offset < payload.Length; offset += perChunk)
        {
            int length = Math.Min(perChunk, payload.Length - offset);
            byte[] compressed = Deflate(payload.AsSpan(offset, length));

            byte[] header = new byte[ChunkHeaderLength];
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x00), ContainerMagic);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x08), BlockSize);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x10), (ulong)compressed.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x18), (ulong)length);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x20), (ulong)compressed.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(header.AsSpan(0x28), (ulong)length);
            file.Write(header);
            file.Write(compressed);
        }

        return file.ToArray();
    }

    private static byte[] Deflate(ReadOnlySpan<byte> data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return compressed.ToArray();
    }
}
