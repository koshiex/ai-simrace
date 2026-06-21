using System.Buffers.Binary;
using FluentAssertions;
using SimCoach.Storage.Mcap;
using Xunit;

namespace SimCoach.Storage.Tests;

/// <summary>
/// Tests for the B5 closeout: zstd chunk compression and the MCAP summary section
/// (Statistics + ChunkIndex + repeated Schema/Channel + SummaryOffset + populated Footer).
/// Footer field offsets follow the spec (https://mcap.dev/spec): the trailing Footer record is
/// opcode(1) + length(8) + summary_start(8) + summary_offset_start(8) + summary_crc32(4),
/// followed by the 8-byte magic.
/// </summary>
public sealed class McapZstdSummaryTests
{
    private const byte StatisticsOpcode = 0x0B;

    [Fact]
    public void Zstd_messages_roundtrip_byte_identical()
    {
        // Arrange
        byte[] firstPayload = [1, 2, 3, 4, 5];
        byte[] secondPayload = [9, 8, 7];
        byte[] file = WriteZstdFile(writer =>
        {
            ushort schemaId = writer.AddSchema("simcoach.Test", "protobuf", [0xAA, 0xBB]);
            ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
            writer.WriteMessage(channelId, 0, 1_000, 1_000, firstPayload);
            writer.WriteMessage(channelId, 1, 2_000, 2_500, secondPayload);
        });

        // Act
        McapSegment segment = ReadFile(file);

        // Assert
        segment.Messages.Should().HaveCount(2);
        segment.Messages[0].Data.Should().Equal(firstPayload);
        segment.Messages[1].Data.Should().Equal(secondPayload);
        segment.Messages[1].PublishTimeNs.Should().Be(2_500UL);
    }

    [Fact]
    public void Zstd_chunk_uses_zstd_and_compresses_repetitive_payloads()
    {
        // Arrange — highly compressible payload so the zstd file is clearly smaller
        byte[] payload = new byte[4096];
        Array.Fill(payload, (byte)0x5A);

        byte[] zstdFile = WriteZstdFile(writer => WriteOne(writer, payload));
        byte[] plainFile = WritePlainFile(writer => WriteOne(writer, payload));

        // Assert
        zstdFile.Length.Should().BeLessThan(plainFile.Length);
        ReadFile(zstdFile).Messages.Should().ContainSingle().Which.Data.Should().Equal(payload);
    }

    [Fact]
    public void Zstd_chunk_corruption_is_detected()
    {
        // Arrange — a large, low-compressibility payload so the compressed chunk (near the front,
        // before DataEnd) dominates the file; corrupt in its first quarter so the damage lands in
        // the chunk records, not the trailing summary section (which the reader skips).
        byte[] payload = new byte[16384];
        for (int index = 0; index < payload.Length; index++)
        {
            payload[index] = (byte)(index * 31 + 7);
        }

        byte[] file = WriteZstdFile(writer => WriteOne(writer, payload));
        int corruptAt = file.Length / 4;
        file[corruptAt] ^= 0xFF;

        // Act
        Action act = () => ReadFile(file);

        // Assert — either decompression fails or the CRC over the decompressed records mismatches
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Footer_summary_start_is_populated_when_records_exist()
    {
        // Arrange
        byte[] file = WriteZstdFile(writer => WriteOne(writer, [7, 7, 7]));

        // Act
        ulong summaryStart = ReadUInt64(file, file.Length - 28);
        ulong summaryOffsetStart = ReadUInt64(file, file.Length - 20);

        // Assert
        summaryStart.Should().BeGreaterThan(0);
        summaryOffsetStart.Should().BeGreaterThan(summaryStart);
    }

    [Fact]
    public void Empty_file_keeps_a_zeroed_footer()
    {
        // Arrange — no schema/channel/message means no summary section
        using var stream = new MemoryStream();
        using (var writer = new McapWriter(stream, new McapWriterOptions { LeaveOpen = true }))
        {
            writer.Finish();
        }

        byte[] file = stream.ToArray();

        // Assert
        ReadUInt64(file, file.Length - 28).Should().Be(0UL); // summary_start
        ReadUInt64(file, file.Length - 20).Should().Be(0UL); // summary_offset_start
    }

    [Fact]
    public void Reading_a_summary_file_does_not_duplicate_schemas_or_channels()
    {
        // Arrange — the summary section repeats Schema/Channel records; the reader must stop at DataEnd
        byte[] file = WriteZstdFile(writer =>
        {
            ushort schemaId = writer.AddSchema("simcoach.Test", "protobuf", [0x01]);
            ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
            writer.WriteMessage(channelId, 0, 10, 10, [1]);
        });

        // Act
        McapSegment segment = ReadFile(file);

        // Assert
        segment.Schemas.Should().ContainSingle();
        segment.Channels.Should().ContainSingle();
    }

    [Fact]
    public void Statistics_record_counts_match_written_messages()
    {
        // Arrange
        byte[] file = WriteZstdFile(writer =>
        {
            ushort schemaId = writer.AddSchema("simcoach.Test", "protobuf", []);
            ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
            for (uint sequence = 0; sequence < 5; sequence++)
            {
                writer.WriteMessage(channelId, sequence, sequence * 10, sequence * 10, [(byte)sequence]);
            }
        });

        // Act
        ulong messageCount = ReadStatisticsMessageCount(file);

        // Assert
        messageCount.Should().Be(5UL);
    }

    [Fact]
    public void Zstd_roundtrips_across_multiple_chunks()
    {
        // Arrange — a small threshold forces several chunks, each compressed and indexed
        using var stream = new MemoryStream();
        using (var writer = new McapWriter(
                   stream,
                   new McapWriterOptions { Compression = McapCompression.Zstd, ChunkThresholdBytes = 64, LeaveOpen = true }))
        {
            ushort schemaId = writer.AddSchema("simcoach.Test", "protobuf", []);
            ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
            for (uint sequence = 0; sequence < 10; sequence++)
            {
                writer.WriteMessage(channelId, sequence, sequence * 100, sequence * 100, new byte[32]);
            }

            writer.Finish();
        }

        // Act
        McapSegment segment = ReadFile(stream.ToArray());

        // Assert
        segment.ChunkCount.Should().BeGreaterThan(1);
        segment.Messages.Should().HaveCount(10);
        segment.Messages.Select(message => message.Sequence)
            .Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Huge_chunk_uncompressed_size_is_invalid_data_not_overflow()
    {
        // Arrange — patch the chunk's uncompressed_size field to a value past int.MaxValue
        byte[] file = WriteZstdFile(writer => WriteOne(writer, [1, 2, 3]));
        int uncompressedSizeOffset = FindChunkContentStart(file) + (sizeof(ulong) * 2); // after start/end times
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(uncompressedSizeOffset, sizeof(ulong)), (ulong)int.MaxValue + 1);

        // Act
        Action act = () => ReadFile(file);

        // Assert — the cast-before-allocate guard surfaces this as InvalidDataException, not OverflowException
        act.Should().Throw<InvalidDataException>().WithMessage("*uncompressed_size*");
    }

    /// <summary>Walks the top-level record framing to the first Chunk record's content start.</summary>
    private static int FindChunkContentStart(byte[] file)
    {
        const byte chunkOpcode = 0x06;
        int position = 8; // skip leading magic
        while (position < file.Length)
        {
            byte opcode = file[position];
            ulong contentLength = ReadUInt64(file, position + 1);
            int contentStart = position + 1 + sizeof(ulong);
            if (opcode == chunkOpcode)
            {
                return contentStart;
            }

            position = contentStart + (int)contentLength;
        }

        throw new InvalidOperationException("No Chunk record found in the file.");
    }

    private static void WriteOne(McapWriter writer, byte[] payload)
    {
        ushort schemaId = writer.AddSchema("simcoach.Test", "protobuf", [0x01]);
        ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
        writer.WriteMessage(channelId, 0, 42, 42, payload);
    }

    private static byte[] WriteZstdFile(Action<McapWriter> write) =>
        WriteFile(McapCompression.Zstd, write);

    private static byte[] WritePlainFile(Action<McapWriter> write) =>
        WriteFile(McapCompression.None, write);

    private static byte[] WriteFile(McapCompression compression, Action<McapWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new McapWriter(
                   stream, new McapWriterOptions { Compression = compression, LeaveOpen = true }))
        {
            write(writer);
            writer.Finish();
        }

        return stream.ToArray();
    }

    private static McapSegment ReadFile(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return McapSegment.Read(stream);
    }

    private static ulong ReadUInt64(byte[] file, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(offset, sizeof(ulong)));

    /// <summary>Walks the summary section record framing to the Statistics record's message_count.</summary>
    private static ulong ReadStatisticsMessageCount(byte[] file)
    {
        int position = (int)ReadUInt64(file, file.Length - 28); // summary_start
        while (position < file.Length)
        {
            byte opcode = file[position];
            position++;
            ulong contentLength = ReadUInt64(file, position);
            position += sizeof(ulong);
            if (opcode == StatisticsOpcode)
            {
                return ReadUInt64(file, position); // message_count is the first Statistics field
            }

            position += (int)contentLength;
        }

        throw new InvalidOperationException("Statistics record not found in the summary section.");
    }
}
