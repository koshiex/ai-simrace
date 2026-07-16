using System.Buffers.Binary;
using System.Text;

namespace SimCoach.GhostImport;

/// <summary>
/// Parses the decompressed ghost payload (<c>docs/05-implementation/acc-ghost-format-re.md</c>): a
/// fixed header (length echo, version, track-id string, record count) followed by fixed-stride 130-byte
/// records and an 11-byte trailer. Little-endian throughout. LINE-only: X/Y/Z/yaw/pedals are read; the
/// undecodable channels (gear/steer/clutch) and the logarithmic clock are not interpreted here.
/// </summary>
internal static class GhostPayload
{
    /// <summary>Fixed record stride, found by byte autocorrelation (peaks at lag 130/260).</summary>
    internal const int RecordStride = 130;

    /// <summary>Trailer bytes after the last record (<c>u32 3</c> then zeros).</summary>
    internal const int TrailerLength = 11;

    private const int VersionOffset = 4;
    private const int ExpectedVersion = 4;
    private const int StringLengthOffset = 17;
    private const int TrackIdOffset = 21;

    private const int RecordWorldXOffset = 0;
    private const int RecordWorldYOffset = 4;
    private const int RecordWorldZOffset = 8;
    private const int RecordYawOffset = 12;
    private const int RecordBrakeOffset = 24;
    private const int RecordThrottleOffset = 25;
    private const int RecordTimestampOffset = 126;

    internal static GhostPayloadHeader ReadHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < TrackIdOffset + sizeof(int))
        {
            throw new InvalidDataException($"ghost payload too short ({payload.Length} bytes) to hold a header");
        }

        uint declaredFollowing = BinaryPrimitives.ReadUInt32LittleEndian(payload);
        if (declaredFollowing != payload.Length - sizeof(uint))
        {
            throw new InvalidDataException(
                $"ghost payload length echo mismatch: header says {declaredFollowing}, "
                + $"payload holds {payload.Length - sizeof(uint)} following bytes");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(payload[VersionOffset..]);
        if (version != ExpectedVersion)
        {
            throw new InvalidDataException($"ghost payload version {version}, expected {ExpectedVersion}");
        }

        uint stringLength = BinaryPrimitives.ReadUInt32LittleEndian(payload[StringLengthOffset..]);
        if (stringLength == 0 || TrackIdOffset + stringLength + sizeof(uint) > (uint)payload.Length)
        {
            throw new InvalidDataException($"ghost payload track-id string length {stringLength} is out of range");
        }

        // The stored string includes a trailing NUL; keep only the characters before it.
        ReadOnlySpan<byte> stringBytes = payload.Slice(TrackIdOffset, (int)stringLength);
        int nul = stringBytes.IndexOf((byte)0);
        string trackId = Encoding.ASCII.GetString(nul >= 0 ? stringBytes[..nul] : stringBytes);

        int countOffset = TrackIdOffset + (int)stringLength;
        int recordCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[countOffset..]);
        if (recordCount < 0)
        {
            throw new InvalidDataException($"ghost payload declares a negative record count {recordCount}");
        }

        int recordStart = countOffset + sizeof(uint);
        return new GhostPayloadHeader(trackId, recordCount, recordStart, payload.Length);
    }

    internal static IReadOnlyList<GhostRecord> ReadRecords(ReadOnlySpan<byte> payload, GhostPayloadHeader header)
    {
        long recordsEnd = (long)header.RecordStart + (long)header.RecordCount * RecordStride;
        if (recordsEnd > payload.Length)
        {
            throw new InvalidDataException(
                $"ghost records run to {recordsEnd} past the payload length {payload.Length}");
        }

        var records = new GhostRecord[header.RecordCount];
        for (int i = 0; i < header.RecordCount; i++)
        {
            ReadOnlySpan<byte> record = payload.Slice(header.RecordStart + (i * RecordStride), RecordStride);
            records[i] = new GhostRecord(
                WorldX: BinaryPrimitives.ReadSingleLittleEndian(record[RecordWorldXOffset..]),
                WorldY: BinaryPrimitives.ReadSingleLittleEndian(record[RecordWorldYOffset..]),
                WorldZ: BinaryPrimitives.ReadSingleLittleEndian(record[RecordWorldZOffset..]),
                Yaw: BinaryPrimitives.ReadSingleLittleEndian(record[RecordYawOffset..]),
                BrakeNorm: record[RecordBrakeOffset] / 255f,
                ThrottleNorm: record[RecordThrottleOffset] / 255f,
                RawTimestamp: BinaryPrimitives.ReadSingleLittleEndian(record[RecordTimestampOffset..]));
        }

        return records;
    }
}
