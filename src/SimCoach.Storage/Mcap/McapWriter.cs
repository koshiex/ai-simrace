using System.IO.Hashing;
using ZstdSharp;

namespace SimCoach.Storage.Mcap;

/// <summary>
/// Minimal MCAP writer per https://mcap.dev/spec, hand-rolled because no C# MCAP package exists
/// on NuGet (ADR-0003 risk mitigation). Produces: magic, Header, Schema/Channel/Message records
/// grouped into Chunk records (optionally zstd-compressed) with a CRC32 over the uncompressed
/// records, MessageIndex records per chunk, DataEnd, then a summary section (repeated
/// Schema/Channel, Statistics, ChunkIndex), a summary-offset section and a populated Footer —
/// so the standard `mcap` CLI and Foxglove can index and seek the file.
/// Requires a seekable output stream (it records byte offsets for the summary).
/// Not thread-safe; call from one thread.
/// </summary>
public sealed class McapWriter : IDisposable
{
    private readonly Stream _stream;
    private readonly McapWriterOptions _options;
    private readonly string _compressionName;
    private readonly MemoryStream _chunkBuffer = new();

    // Per-chunk message index: channel id -> (log time, offset within the uncompressed records).
    private readonly Dictionary<ushort, List<(ulong LogTime, ulong Offset)>> _chunkMessageIndex = [];

    // Summary state accumulated across all chunks.
    private readonly List<byte[]> _schemaRecordContents = [];
    private readonly List<byte[]> _channelRecordContents = [];
    private readonly List<byte[]> _chunkIndexContents = [];
    private readonly Dictionary<ushort, ulong> _channelMessageCounts = [];
    private ulong _messageCount;
    private bool _statsHasMessages;
    private ulong _statsMessageStartTimeNs;
    private ulong _statsMessageEndTimeNs;

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
        if (!stream.CanSeek)
        {
            throw new ArgumentException(
                "MCAP output stream must be seekable — the summary section records byte offsets.",
                nameof(stream));
        }

        _stream = stream;
        _compressionName = _options.Compression == McapCompression.Zstd ? "zstd" : string.Empty;

        _stream.Write(McapFormat.Magic);
        using var content = new MemoryStream();
        McapFormat.WriteString(content, _options.Profile);
        McapFormat.WriteString(content, _options.Library);
        WriteRecordFrom(_stream, McapFormat.HeaderOpcode, content);
    }

    /// <summary>Registers a schema; the record lands in the current chunk and the summary.</summary>
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
        byte[] contentBytes = content.ToArray();
        McapFormat.WriteRecord(_chunkBuffer, McapFormat.SchemaOpcode, contentBytes);
        _schemaRecordContents.Add(contentBytes);
        return schemaId;
    }

    /// <summary>Registers a channel; the record lands in the current chunk and the summary.</summary>
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
        byte[] contentBytes = content.ToArray();
        McapFormat.WriteRecord(_chunkBuffer, McapFormat.ChannelOpcode, contentBytes);
        _channelRecordContents.Add(contentBytes);
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

        // Offset of this message record within the uncompressed chunk records (for MessageIndex).
        ulong offsetInRecords = (ulong)_chunkBuffer.Length;

        using var content = new MemoryStream();
        McapFormat.WriteUInt16(content, channelId);
        McapFormat.WriteUInt32(content, sequence);
        McapFormat.WriteUInt64(content, logTimeNs);
        McapFormat.WriteUInt64(content, publishTimeNs);
        content.Write(data);
        WriteRecordFrom(_chunkBuffer, McapFormat.MessageOpcode, content);

        if (!_chunkMessageIndex.TryGetValue(channelId, out List<(ulong, ulong)>? entries))
        {
            entries = [];
            _chunkMessageIndex[channelId] = entries;
        }

        entries.Add((logTimeNs, offsetInRecords));

        TrackMessageStats(channelId, logTimeNs);
        TrackChunkTimes(logTimeNs);

        if (_chunkBuffer.Length >= _options.ChunkThresholdBytes)
        {
            FlushChunk();
        }
    }

    /// <summary>Flushes the last chunk and writes DataEnd, the summary section and the Footer.</summary>
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
            WriteRecordFrom(_stream, McapFormat.DataEndOpcode, dataEnd);
        }

        bool hasSummary = _schemaRecordContents.Count > 0
            || _channelRecordContents.Count > 0
            || _chunkIndexContents.Count > 0;
        if (hasSummary)
        {
            WriteSummaryAndFooter();
        }
        else
        {
            WriteEmptyFooter();
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
            _chunkMessageIndex.Clear();
            return;
        }

        ReadOnlySpan<byte> records = _chunkBuffer.GetBuffer().AsSpan(0, (int)_chunkBuffer.Length);
        ulong uncompressedSize = (ulong)records.Length;
        uint uncompressedCrc = Crc32.HashToUInt32(records); // spec: CRC over the uncompressed records
        byte[] stored = _options.Compression == McapCompression.Zstd ? Compress(records) : records.ToArray();

        ulong chunkStartOffset = (ulong)_stream.Position;
        using (var content = new MemoryStream())
        {
            McapFormat.WriteUInt64(content, _chunkHasMessages ? _chunkMessageStartTimeNs : 0);
            McapFormat.WriteUInt64(content, _chunkHasMessages ? _chunkMessageEndTimeNs : 0);
            McapFormat.WriteUInt64(content, uncompressedSize);
            McapFormat.WriteUInt32(content, uncompressedCrc);
            McapFormat.WriteString(content, _compressionName);
            McapFormat.WriteUInt64(content, (ulong)stored.Length);
            content.Write(stored);
            WriteRecordFrom(_stream, McapFormat.ChunkOpcode, content);
        }

        ulong chunkLength = (ulong)_stream.Position - chunkStartOffset;
        (List<KeyValuePair<ushort, ulong>> offsets, ulong indexLength) = WriteMessageIndexes();

        _chunkIndexContents.Add(BuildChunkIndex(
            chunkStartOffset, chunkLength, offsets, indexLength, (ulong)stored.Length, uncompressedSize));

        _chunkBuffer.SetLength(0);
        _chunkHasMessages = false;
        _chunkMessageIndex.Clear();
    }

    /// <summary>Writes a MessageIndex record per channel and returns their file offsets + total length.</summary>
    private (List<KeyValuePair<ushort, ulong>> Offsets, ulong IndexLength) WriteMessageIndexes()
    {
        List<KeyValuePair<ushort, ulong>> offsets = [];
        ulong indexLength = 0;
        foreach (ushort channelId in _chunkMessageIndex.Keys.Order())
        {
            ulong recordOffset = (ulong)_stream.Position;
            using var content = new MemoryStream();
            McapFormat.WriteUInt16(content, channelId);
            McapFormat.WriteUInt64PairArray(content, _chunkMessageIndex[channelId]);
            WriteRecordFrom(_stream, McapFormat.MessageIndexOpcode, content);
            offsets.Add(new KeyValuePair<ushort, ulong>(channelId, recordOffset));
            indexLength += (ulong)_stream.Position - recordOffset;
        }

        return (offsets, indexLength);
    }

    private byte[] BuildChunkIndex(
        ulong chunkStartOffset,
        ulong chunkLength,
        IReadOnlyCollection<KeyValuePair<ushort, ulong>> messageIndexOffsets,
        ulong messageIndexLength,
        ulong compressedSize,
        ulong uncompressedSize)
    {
        using var content = new MemoryStream();
        McapFormat.WriteUInt64(content, _chunkHasMessages ? _chunkMessageStartTimeNs : 0);
        McapFormat.WriteUInt64(content, _chunkHasMessages ? _chunkMessageEndTimeNs : 0);
        McapFormat.WriteUInt64(content, chunkStartOffset);
        McapFormat.WriteUInt64(content, chunkLength);
        McapFormat.WriteUInt16ToUInt64Map(content, messageIndexOffsets);
        McapFormat.WriteUInt64(content, messageIndexLength);
        McapFormat.WriteString(content, _compressionName);
        McapFormat.WriteUInt64(content, compressedSize);
        McapFormat.WriteUInt64(content, uncompressedSize);
        return content.ToArray();
    }

    private void WriteSummaryAndFooter()
    {
        ulong baseOffset = (ulong)_stream.Position;
        using var tail = new MemoryStream();

        ulong summaryStart = baseOffset;

        ulong schemaStart = baseOffset + (ulong)tail.Position;
        foreach (byte[] content in _schemaRecordContents)
        {
            McapFormat.WriteRecord(tail, McapFormat.SchemaOpcode, content);
        }

        ulong schemaLength = baseOffset + (ulong)tail.Position - schemaStart;

        ulong channelStart = baseOffset + (ulong)tail.Position;
        foreach (byte[] content in _channelRecordContents)
        {
            McapFormat.WriteRecord(tail, McapFormat.ChannelOpcode, content);
        }

        ulong channelLength = baseOffset + (ulong)tail.Position - channelStart;

        ulong statsStart = baseOffset + (ulong)tail.Position;
        WriteStatistics(tail);
        ulong statsLength = baseOffset + (ulong)tail.Position - statsStart;

        ulong chunkIndexStart = baseOffset + (ulong)tail.Position;
        foreach (byte[] content in _chunkIndexContents)
        {
            McapFormat.WriteRecord(tail, McapFormat.ChunkIndexOpcode, content);
        }

        ulong chunkIndexLength = baseOffset + (ulong)tail.Position - chunkIndexStart;

        ulong summaryOffsetStart = baseOffset + (ulong)tail.Position;
        WriteSummaryOffset(tail, McapFormat.SchemaOpcode, schemaStart, schemaLength, _schemaRecordContents.Count);
        WriteSummaryOffset(tail, McapFormat.ChannelOpcode, channelStart, channelLength, _channelRecordContents.Count);
        WriteSummaryOffset(tail, McapFormat.StatisticsOpcode, statsStart, statsLength, 1);
        WriteSummaryOffset(tail, McapFormat.ChunkIndexOpcode, chunkIndexStart, chunkIndexLength, _chunkIndexContents.Count);

        // Footer record written by hand: summary_crc32 covers everything up to (but not including) itself.
        tail.WriteByte(McapFormat.FooterOpcode);
        McapFormat.WriteUInt64(tail, 8 + 8 + 4); // content length: summary_start + summary_offset_start + crc
        McapFormat.WriteUInt64(tail, summaryStart);
        McapFormat.WriteUInt64(tail, summaryOffsetStart);
        uint summaryCrc = Crc32.HashToUInt32(tail.GetBuffer().AsSpan(0, (int)tail.Length));
        McapFormat.WriteUInt32(tail, summaryCrc);

        _stream.Write(tail.GetBuffer().AsSpan(0, (int)tail.Length));
    }

    private void WriteStatistics(Stream target)
    {
        using var content = new MemoryStream();
        McapFormat.WriteUInt64(content, _messageCount);
        McapFormat.WriteUInt16(content, (ushort)_schemaRecordContents.Count);
        McapFormat.WriteUInt32(content, (uint)_channelRecordContents.Count);
        McapFormat.WriteUInt32(content, 0); // attachment_count
        McapFormat.WriteUInt32(content, 0); // metadata_count
        McapFormat.WriteUInt32(content, (uint)_chunkIndexContents.Count);
        McapFormat.WriteUInt64(content, _statsHasMessages ? _statsMessageStartTimeNs : 0);
        McapFormat.WriteUInt64(content, _statsHasMessages ? _statsMessageEndTimeNs : 0);
        var counts = _channelMessageCounts.Keys.Order()
            .Select(channelId => new KeyValuePair<ushort, ulong>(channelId, _channelMessageCounts[channelId]))
            .ToList();
        McapFormat.WriteUInt16ToUInt64Map(content, counts);
        WriteRecordFrom(target, McapFormat.StatisticsOpcode, content);
    }

    private static void WriteSummaryOffset(
        Stream target, byte groupOpcode, ulong groupStart, ulong groupLength, int recordCount)
    {
        if (recordCount == 0)
        {
            return;
        }

        using var content = new MemoryStream();
        content.WriteByte(groupOpcode);
        McapFormat.WriteUInt64(content, groupStart);
        McapFormat.WriteUInt64(content, groupLength);
        WriteRecordFrom(target, McapFormat.SummaryOffsetOpcode, content);
    }

    private void WriteEmptyFooter()
    {
        using var footer = new MemoryStream();
        McapFormat.WriteUInt64(footer, 0); // summary_start: no summary section
        McapFormat.WriteUInt64(footer, 0); // summary_offset_start
        McapFormat.WriteUInt32(footer, 0); // summary_crc32
        WriteRecordFrom(_stream, McapFormat.FooterOpcode, footer);
    }

    private void TrackMessageStats(ushort channelId, ulong logTimeNs)
    {
        _messageCount++;
        _channelMessageCounts.TryGetValue(channelId, out ulong existing);
        _channelMessageCounts[channelId] = existing + 1;

        if (!_statsHasMessages)
        {
            _statsMessageStartTimeNs = logTimeNs;
            _statsMessageEndTimeNs = logTimeNs;
            _statsHasMessages = true;
        }
        else
        {
            _statsMessageStartTimeNs = Math.Min(_statsMessageStartTimeNs, logTimeNs);
            _statsMessageEndTimeNs = Math.Max(_statsMessageEndTimeNs, logTimeNs);
        }
    }

    private void TrackChunkTimes(ulong logTimeNs)
    {
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
    }

    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var compressor = new Compressor();
        return compressor.Wrap(data.ToArray()).ToArray();
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
