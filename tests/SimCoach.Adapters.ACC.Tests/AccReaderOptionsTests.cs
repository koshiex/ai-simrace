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

    public static TheoryData<AccReaderOptions> NegativeIntervalOptions => new()
    {
        new AccReaderOptions { PollInterval = TimeSpan.FromMilliseconds(-1) },
        new AccReaderOptions { ReconnectDelay = TimeSpan.FromSeconds(-1) },
        new AccReaderOptions { StaticRefreshInterval = TimeSpan.FromSeconds(-1) },
    };

    [Theory]
    [MemberData(nameof(NegativeIntervalOptions))]
    public void Negative_intervals_fail_fast(AccReaderOptions options)
    {
        // Act
        Action act = options.EnsureValid;

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
