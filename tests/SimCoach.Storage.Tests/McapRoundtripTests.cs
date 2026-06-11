using System.Buffers.Binary;
using FluentAssertions;
using SimCoach.Storage.Mcap;
using Xunit;

namespace SimCoach.Storage.Tests;

/// <summary>
/// Format tests for the hand-rolled MCAP writer/reader pair against the MCAP spec
/// (https://mcap.dev/spec): magic framing, record layout, chunked messages with CRC32,
/// corruption detection. Golden byte values come from the spec, not from our code —
/// do not "fix" them to match the implementation.
/// </summary>
public sealed class McapRoundtripTests
{
    private static readonly byte[] _mcapMagic = [0x89, 0x4D, 0x43, 0x41, 0x50, 0x30, 0x0D, 0x0A];

    [Fact]
    public void File_starts_and_ends_with_the_spec_magic()
    {
        // Act
        byte[] file = WriteFile();

        // Assert
        file[..8].Should().Equal(_mcapMagic);
        file[^8..].Should().Equal(_mcapMagic);
    }

    [Fact]
    public void First_record_is_a_header_with_profile_and_library()
    {
        // Act
        byte[] file = WriteFile();

        // Assert — spec: record = opcode byte + uint64 LE content length; Header opcode = 0x01
        file[8].Should().Be(0x01);
        ulong contentLength = BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(9, 8));
        contentLength.Should().BeGreaterThan(0);

        McapSegment segment = ReadFile(file);
        segment.Profile.Should().Be("x-simcoach"); // spec: custom profiles use the x- prefix
        segment.Library.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Empty_file_without_messages_roundtrips()
    {
        // Arrange
        using var stream = new MemoryStream();
        using (var writer = new McapWriter(stream, new McapWriterOptions { LeaveOpen = true }))
        {
            writer.Finish();
        }

        // Act
        McapSegment segment = ReadFile(stream.ToArray());

        // Assert
        segment.Schemas.Should().BeEmpty();
        segment.Channels.Should().BeEmpty();
        segment.Messages.Should().BeEmpty();
    }

    [Fact]
    public void Messages_roundtrip_byte_identical_with_metadata()
    {
        // Arrange
        byte[] firstPayload = [1, 2, 3, 4, 5];
        byte[] secondPayload = [9, 8, 7];
        using var stream = new MemoryStream();
        using (var writer = new McapWriter(stream, new McapWriterOptions { LeaveOpen = true }))
        {
            ushort schemaId = writer.AddSchema("simcoach.Test", "protobuf", [0xAA, 0xBB]);
            ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
            writer.WriteMessage(channelId, sequence: 0, logTimeNs: 1_000, publishTimeNs: 1_000, firstPayload);
            writer.WriteMessage(channelId, sequence: 1, logTimeNs: 2_000, publishTimeNs: 2_500, secondPayload);
            writer.Finish();
        }

        // Act
        McapSegment segment = ReadFile(stream.ToArray());

        // Assert
        segment.Schemas.Should().ContainSingle();
        segment.Schemas[0].Name.Should().Be("simcoach.Test");
        segment.Schemas[0].Encoding.Should().Be("protobuf");
        segment.Schemas[0].Data.Should().Equal(0xAA, 0xBB);
        segment.Channels.Should().ContainSingle();
        segment.Channels[0].SchemaId.Should().Be(segment.Schemas[0].Id);
        segment.Channels[0].Topic.Should().Be("telemetry");
        segment.Messages.Should().HaveCount(2);
        segment.Messages[0].Data.Should().Equal(firstPayload);
        segment.Messages[0].LogTimeNs.Should().Be(1_000UL);
        segment.Messages[1].Data.Should().Equal(secondPayload);
        segment.Messages[1].Sequence.Should().Be(1U);
        segment.Messages[1].PublishTimeNs.Should().Be(2_500UL);
    }

    [Fact]
    public void Corrupted_chunk_payload_fails_crc_validation()
    {
        // Arrange — distinctive payload so the corruption lands inside chunk records
        byte[] payload = new byte[64];
        Array.Fill(payload, (byte)0xAB);
        byte[] file = WriteFile(payload);
        int payloadOffset = file.AsSpan().IndexOf(payload.AsSpan(0, 16));
        payloadOffset.Should().BeGreaterThan(8, "the payload must sit inside the data section");
        file[payloadOffset + 4] ^= 0xFF;

        // Act
        Action act = () => ReadFile(file);

        // Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*CRC*");
    }

    [Fact]
    public void Small_chunk_threshold_produces_multiple_chunks_and_preserves_order()
    {
        // Arrange
        using var stream = new MemoryStream();
        using (var writer = new McapWriter(
                   stream, new McapWriterOptions { ChunkThresholdBytes = 64, LeaveOpen = true }))
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
    public void Huge_declared_record_length_is_invalid_data_not_overflow()
    {
        // Arrange — first record's uint64 length field (offset 9) patched to ulong.MaxValue
        byte[] file = WriteFile();
        BinaryPrimitives.WriteUInt64LittleEndian(file.AsSpan(9, 8), ulong.MaxValue);

        // Act
        Action act = () => ReadFile(file);

        // Assert — corrupt-segment handlers catch InvalidDataException, not OverflowException
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Huge_declared_string_length_is_invalid_data_not_overflow()
    {
        // Arrange — header profile string length (offset 17) patched to uint.MaxValue
        byte[] file = WriteFile();
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(17, 4), uint.MaxValue);

        // Act
        Action act = () => ReadFile(file);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Truncated_file_throws_a_clear_error()
    {
        // Arrange
        byte[] file = WriteFile();
        byte[] truncated = file[..(file.Length / 2)];

        // Act
        Action act = () => ReadFile(truncated);

        // Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void File_without_leading_magic_is_rejected()
    {
        // Act — long enough to pass the size check, but no MCAP magic
        Action act = () => ReadFile(new byte[32]);

        // Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*magic*");
    }

    [Fact]
    public void Unknown_top_level_record_is_skipped_for_forward_compatibility()
    {
        // Arrange — splice a fake record (opcode 0x7F, 4 content bytes) right after the header
        byte[] file = WriteFile([1, 2, 3]);
        int headerEnd = 8 + 1 + 8 + (int)BinaryPrimitives.ReadUInt64LittleEndian(file.AsSpan(9, 8));
        byte[] fakeRecord = new byte[1 + 8 + 4];
        fakeRecord[0] = 0x7F;
        BinaryPrimitives.WriteUInt64LittleEndian(fakeRecord.AsSpan(1, 8), 4);
        byte[] spliced = [.. file[..headerEnd], .. fakeRecord, .. file[headerEnd..]];

        // Act
        McapSegment segment = ReadFile(spliced);

        // Assert
        segment.Messages.Should().ContainSingle().Which.Data.Should().Equal(1, 2, 3);
    }

    private static byte[] WriteFile(byte[]? payload = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new McapWriter(stream, new McapWriterOptions { LeaveOpen = true }))
        {
            ushort schemaId = writer.AddSchema("simcoach.Test", "protobuf", [0x01]);
            ushort channelId = writer.AddChannel(schemaId, "telemetry", "protobuf");
            writer.WriteMessage(channelId, sequence: 0, logTimeNs: 42, publishTimeNs: 42, payload ?? [7, 7, 7]);
            writer.Finish();
        }

        return stream.ToArray();
    }

    private static McapSegment ReadFile(byte[] file)
    {
        using var stream = new MemoryStream(file);
        return McapSegment.Read(stream);
    }
}
