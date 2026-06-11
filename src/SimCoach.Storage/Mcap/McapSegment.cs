using System.IO.Hashing;

namespace SimCoach.Storage.Mcap;

public sealed record McapSchema(ushort Id, string Name, string Encoding, byte[] Data);

public sealed record McapChannel(ushort Id, ushort SchemaId, string Topic, string MessageEncoding);

public sealed record McapMessage(ushort ChannelId, uint Sequence, ulong LogTimeNs, ulong PublishTimeNs, byte[] Data);

/// <summary>
/// Minimal MCAP reader for files produced by <see cref="McapWriter"/> (and any spec-compliant
/// writer that uses uncompressed chunks). Loads a whole segment into memory — recorder segments
/// are ~60 s of telemetry, a few megabytes. Verifies magic framing and chunk CRC32; skips
/// unknown record types for forward compatibility.
/// </summary>
public sealed class McapSegment
{
    private readonly List<McapSchema> _schemas = [];
    private readonly List<McapChannel> _channels = [];
    private readonly List<McapMessage> _messages = [];

    private McapSegment()
    {
    }

    public string Profile { get; private set; } = string.Empty;

    public string Library { get; private set; } = string.Empty;

    public int ChunkCount { get; private set; }

    public IReadOnlyList<McapSchema> Schemas => _schemas;

    public IReadOnlyList<McapChannel> Channels => _channels;

    public IReadOnlyList<McapMessage> Messages => _messages;

    /// <exception cref="InvalidDataException">Malformed, truncated or corrupted file.</exception>
    /// <exception cref="NotSupportedException">Compressed chunks (not produced by our writer).</exception>
    public static McapSegment Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        byte[] file = buffer.ToArray();

        if (file.Length < McapFormat.Magic.Length * 2)
        {
            throw new InvalidDataException($"File of {file.Length} bytes is too small to be MCAP.");
        }

        if (!file.AsSpan(0, McapFormat.Magic.Length).SequenceEqual(McapFormat.Magic))
        {
            throw new InvalidDataException("Leading MCAP magic bytes are missing.");
        }

        if (!file.AsSpan(file.Length - McapFormat.Magic.Length).SequenceEqual(McapFormat.Magic))
        {
            throw new InvalidDataException("Trailing MCAP magic bytes are missing.");
        }

        McapSegment segment = new();
        segment.ParseRecords(
            file.AsSpan(McapFormat.Magic.Length, file.Length - 2 * McapFormat.Magic.Length),
            isTopLevel: true);
        return segment;
    }

    private void ParseRecords(ReadOnlySpan<byte> records, bool isTopLevel)
    {
        int position = 0;
        while (position < records.Length)
        {
            McapFormat.EnsureAvailable(records, position, 1 + sizeof(ulong));
            byte opcode = records[position];
            position++;
            int contentLength = McapFormat.ReadLength64(records, ref position);
            ReadOnlySpan<byte> content = records.Slice(position, contentLength);
            position += contentLength;

            switch (opcode)
            {
                case McapFormat.HeaderOpcode:
                    ParseHeader(content);
                    break;
                case McapFormat.SchemaOpcode:
                    ParseSchema(content);
                    break;
                case McapFormat.ChannelOpcode:
                    ParseChannel(content);
                    break;
                case McapFormat.MessageOpcode:
                    ParseMessage(content);
                    break;
                case McapFormat.ChunkOpcode when isTopLevel:
                    ParseChunk(content);
                    break;
                case McapFormat.DataEndOpcode:
                case McapFormat.FooterOpcode:
                    break;
                default:
                    break; // unknown record — skip for forward compatibility
            }
        }
    }

    private void ParseHeader(ReadOnlySpan<byte> content)
    {
        int position = 0;
        Profile = McapFormat.ReadString(content, ref position);
        Library = McapFormat.ReadString(content, ref position);
    }

    private void ParseSchema(ReadOnlySpan<byte> content)
    {
        int position = 0;
        ushort id = McapFormat.ReadUInt16(content, ref position);
        string name = McapFormat.ReadString(content, ref position);
        string encoding = McapFormat.ReadString(content, ref position);
        int dataLength = McapFormat.ReadLength32(content, ref position);
        byte[] data = McapFormat.ReadBytes(content, ref position, dataLength);
        _schemas.Add(new McapSchema(id, name, encoding, data));
    }

    private void ParseChannel(ReadOnlySpan<byte> content)
    {
        int position = 0;
        ushort id = McapFormat.ReadUInt16(content, ref position);
        ushort schemaId = McapFormat.ReadUInt16(content, ref position);
        string topic = McapFormat.ReadString(content, ref position);
        string messageEncoding = McapFormat.ReadString(content, ref position);
        _channels.Add(new McapChannel(id, schemaId, topic, messageEncoding));
    }

    private void ParseMessage(ReadOnlySpan<byte> content)
    {
        int position = 0;
        ushort channelId = McapFormat.ReadUInt16(content, ref position);
        uint sequence = McapFormat.ReadUInt32(content, ref position);
        ulong logTimeNs = McapFormat.ReadUInt64(content, ref position);
        ulong publishTimeNs = McapFormat.ReadUInt64(content, ref position);
        byte[] data = McapFormat.ReadBytes(content, ref position, content.Length - position);
        _messages.Add(new McapMessage(channelId, sequence, logTimeNs, publishTimeNs, data));
    }

    private void ParseChunk(ReadOnlySpan<byte> content)
    {
        int position = 0;
        _ = McapFormat.ReadUInt64(content, ref position); // message_start_time
        _ = McapFormat.ReadUInt64(content, ref position); // message_end_time
        _ = McapFormat.ReadUInt64(content, ref position); // uncompressed_size
        uint expectedCrc = McapFormat.ReadUInt32(content, ref position);
        string compression = McapFormat.ReadString(content, ref position);
        int recordsLength = McapFormat.ReadLength64(content, ref position);
        ReadOnlySpan<byte> records = content.Slice(position, recordsLength);

        if (compression.Length > 0)
        {
            throw new NotSupportedException(
                $"Chunk compression '{compression}' is not supported by this minimal reader.");
        }

        if (expectedCrc != 0)
        {
            uint actualCrc = Crc32.HashToUInt32(records);
            if (actualCrc != expectedCrc)
            {
                throw new InvalidDataException(
                    $"Chunk CRC mismatch: expected {expectedCrc:X8}, computed {actualCrc:X8} — the file is corrupted.");
            }
        }

        ChunkCount++;
        ParseRecords(records, isTopLevel: false);
    }
}
