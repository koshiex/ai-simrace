using FluentAssertions;
using Xunit;

namespace SimCoach.Adapters.ACC.Tests;

public sealed class AccReaderOptionsTests
{
    [Fact]
    public void Default_options_are_valid()
    {
        // Arrange
        AccReaderOptions options = new();

        // Act
        Action act = options.EnsureValid;

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Zero_seqlock_retries_fail_fast()
    {
        // Arrange — zero retries would silently never produce a frame
        AccReaderOptions options = new() { MaxSeqlockRetries = 0 };

        // Act
        Action act = options.EnsureValid;

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Non_positive_channel_capacity_fails_fast()
    {
        // Arrange
        AccReaderOptions options = new() { ChannelCapacity = 0 };

        // Act
        Action act = options.EnsureValid;

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Negative_intervals_fail_fast()
    {
        // Arrange
        AccReaderOptions options = new() { ReconnectDelay = TimeSpan.FromSeconds(-1) };

        // Act
        Action act = options.EnsureValid;

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
