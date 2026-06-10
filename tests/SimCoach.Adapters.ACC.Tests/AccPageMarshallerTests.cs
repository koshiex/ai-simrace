using FluentAssertions;
using SimCoach.Adapters.ACC.SharedMemory;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

public sealed class AccPageMarshallerTests
{
    [Fact]
    public void Read_throws_when_page_bytes_are_null()
    {
        // Arrange
        byte[]? page = null;

        // Act
        Action act = () => AccPageMarshaller.Read<AccPhysicsPage>(page!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Read_throws_when_buffer_is_smaller_than_page_struct()
    {
        // Arrange
        byte[] tooSmall = new byte[AccPhysicsPage.SizeBytes - 1];

        // Act
        Action act = () => AccPageMarshaller.Read<AccPhysicsPage>(tooSmall);

        // Assert
        act.Should().Throw<ArgumentException>().WithMessage("*799*800*");
    }

    [Fact]
    public void ReadPacketId_reads_leading_int_without_full_marshal()
    {
        // Arrange
        byte[] page = new PageFixtureBuilder(AccPhysicsPage.SizeBytes)
            .WithInt32(0, 123_456)
            .Build();

        // Act
        int packetId = AccPageMarshaller.ReadPacketId(page);

        // Assert
        packetId.Should().Be(123_456);
    }

    [Fact]
    public void ReadPacketId_throws_when_buffer_is_smaller_than_int()
    {
        // Arrange
        byte[] tooSmall = new byte[sizeof(int) - 1];

        // Act
        Action act = () => AccPageMarshaller.ReadPacketId(tooSmall);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Read_accepts_buffer_larger_than_page_struct()
    {
        // Arrange — live MMF views are page-granular, so buffers can exceed the struct size
        byte[] oversized = new byte[AccPhysicsPage.SizeBytes + 64];
        oversized[0] = 42; // packetId low byte

        // Act
        AccPhysicsPage parsed = AccPageMarshaller.Read<AccPhysicsPage>(oversized);

        // Assert
        parsed.PacketId.Should().Be(42);
    }
}
