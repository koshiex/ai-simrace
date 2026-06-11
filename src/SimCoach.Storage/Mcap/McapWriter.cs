using System.IO.Hashing;

namespace SimCoach.Storage.Mcap;

/// <summary>
/// Minimal MCAP writer per https://mcap.dev/spec, hand-rolled because no C# MCAP package exists
/// on NuGet (ADR-0003 risk mitigation). Produces: magic, Header, then Schema/Channel/Message
/// records grouped into uncompressed Chunk records with CRC32, then DataEnd, Footer, magic.
/// No summary section, no compression in v1 — zstd is a documented follow-up.
/// Not thread-safe; call from one thread.
/// </summary>
public sealed class McapWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly McapWriterOptions _options;
    private readonly MemoryStream _chunkBuffer = new();
    private ulong _chunkMessageStartTimeNs;
    private ulong _chunkMessageEndTimeNs;
    private bool _chunkHasMessages;
    private ushort _nextSchemaId = 1; // schema id 0 means "no schema" per spec
    private ushort _nextChannelId = 1;
    private bool _isFinished;

    public McapWriter(Stream stream, McapWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _options = options ?? new McapWriterOptions();
        _options.EnsureValid();
        _stream = stream;

        _stream.Write(McapFormat.Magic);
        using var content = new MemoryStream();
        McapFormat.WriteString(content, _options.Profile);
        McapFormat.WriteString(content, _options.Library);
        WriteRecordFrom(_stream, McapFormat.HeaderOpcode, content);
    }

    /// <summary>Registers a schema; the record lands in the current chunk.</summary>
    public ushort AddSchema(string name, string encoding, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(data);
        EnsureNotFinished();

        ushort schemaId = _nextSchemaId++;
        using var content = new MemoryStream();
        McapFormat.WriteUInt16(content, schemaId);
        McapFormat.WriteString(content, name);
        McapFormat.WriteString(content, encoding);
        McapFormat.WriteUInt32(content, (uint)data.Length);
        content.Write(data);
        WriteRecordFrom(_chunkBuffer, McapFormat.SchemaOpcode, content);
        return schemaId;
    }

    /// <summary>Registers a channel; the record lands in the current chunk.</summary>
    public ushort AddChannel(ushort schemaId, string topic, string messageEncoding)
    {
        ArgumentNullException.ThrowIfNull(topic);
        ArgumentNullException.ThrowIfNull(messageEncoding);
        EnsureNotFinished();

        ushort channelId = _nextChannelId++;
        using var content = new MemoryStream();
        McapFormat.WriteUInt16(content, channelId);
        McapFormat.WriteUInt16(content, schemaId);
        McapFormat.WriteString(content, topic);
        McapFormat.WriteString(content, messageEncoding);
        McapFormat.WriteUInt32(content, 0); // metadata: empty map
        WriteRecordFrom(_chunkBuffer, McapFormat.ChannelOpcode, content);
        return channelId;
    }

    /// <summary>Appends a message; flushes the chunk when it crosses the size threshold.</summary>
    public void WriteMessage(
        ushort channelId,
        uint sequence,
        ulong logTimeNs,
        ulong publishTimeNs,
        ReadOnlySpan<byte> data)
    {
        EnsureNotFinished();
        if (channelId == 0 || channelId >= _nextChannelId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channelId), channelId, "Unknown channel id — register it with AddChannel first.");
        }

        using var content = new MemoryStream();
        McapFormat.WriteUInt16(content, channelId);
        McapFormat.WriteUInt32(content, sequence);
        McapFormat.WriteUInt64(content, logTimeNs);
        McapFormat.WriteUInt64(content, publishTimeNs);
        content.Write(data);
        WriteRecordFrom(_chunkBuffer, McapFormat.MessageOpcode, content);

        if (!_chunkHasMessages)
        {
            _chunkMessageStartTimeNs = logTimeNs;
            _chunkMessageEndTimeNs = logTimeNs;
            _chunkHasMessages = true;
        }
        else
        {
            _chunkMessageStartTimeNs = Math.Min(_chunkMessageStartTimeNs, logTimeNs);
            _chunkMessageEndTimeNs = Math.Max(_chunkMessageEndTimeNs, logTimeNs);
        }

        if (_chunkBuffer.Length >= _options.ChunkThresholdBytes)
        {
            FlushChunk();
        }
    }

    /// <summary>Flushes the last chunk and writes DataEnd, Footer and the trailing magic.</summary>
    public void Finish()
    {
        if (_isFinished)
        {
            return;
        }

        FlushChunk();

        using (var dataEnd = new MemoryStream())
        {
            McapFormat.WriteUInt32(dataEnd, 0); // data_section_crc32: 0 = not computed
            McapFormat.WriteRecord(_stream, McapFormat.DataEndOpcode, dataEnd.GetBuffer().AsSpan(0, (int)dataEnd.Length));
        }

        using (var footer = new MemoryStream())
        {
            McapFormat.WriteUInt64(footer, 0); // summary_start: no summary section
            McapFormat.WriteUInt64(footer, 0); // summary_offset_start
            McapFormat.WriteUInt32(footer, 0); // summary_crc32
            McapFormat.WriteRecord(_stream, McapFormat.FooterOpcode, footer.GetBuffer().AsSpan(0, (int)footer.Length));
        }

        _stream.Write(McapFormat.Magic);
        _stream.Flush();
        _isFinished = true;
    }

    public void Dispose()
    {
        Finish();
        _chunkBuffer.Dispose();
        if (!_options.LeaveOpen)
        {
            _stream.Dispose();
        }
    }

    private void FlushChunk()
    {
        if (_chunkBuffer.Length == 0)
        {
            return;
        }

        ReadOnlySpan<byte> records = _chunkBuffer.GetBuffer().AsSpan(0, (int)_chunkBuffer.Length);
        using var content = new MemoryStream();
        McapFormat.WriteUInt64(content, _chunkHasMessages ? _chunkMessageStartTimeNs : 0);
        McapFormat.WriteUInt64(content, _chunkHasMessages ? _chunkMessageEndTimeNs : 0);
        McapFormat.WriteUInt64(content, (ulong)records.Length); // uncompressed_size
        McapFormat.WriteUInt32(content, Crc32.HashToUInt32(records));
        McapFormat.WriteString(content, string.Empty); // compression: none in v1
        McapFormat.WriteUInt64(content, (ulong)records.Length); // records byte length
        content.Write(records);
        WriteRecordFrom(_stream, McapFormat.ChunkOpcode, content);

        _chunkBuffer.SetLength(0);
        _chunkHasMessages = false;
    }

    private static void WriteRecordFrom(Stream target, byte opcode, MemoryStream content) =>
        McapFormat.WriteRecord(target, opcode, content.GetBuffer().AsSpan(0, (int)content.Length));

    private void EnsureNotFinished()
    {
        if (_isFinished)
        {
            throw new InvalidOperationException("The MCAP file is already finished.");
        }
    }
}
