using FluentAssertions;
using SimCoach.Pipeline;
using Xunit;

namespace SimCoach.Pipeline.Tests;

public sealed class SessionContextTests
{
    [Fact]
    public void Persisted_is_incomplete_until_marked()
    {
        SessionContext context = new();

        context.Persisted.IsCompleted.Should().BeFalse("no session row has been written yet");

        context.MarkPersisted();

        context.Persisted.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public void MarkPersisted_is_idempotent()
    {
        SessionContext context = new();
        context.MarkPersisted();

        Action again = context.MarkPersisted;

        again.Should().NotThrow();
        context.Persisted.IsCompletedSuccessfully.Should().BeTrue();
    }
}
