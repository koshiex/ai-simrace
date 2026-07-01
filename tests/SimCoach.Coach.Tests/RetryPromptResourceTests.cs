using FluentAssertions;
using SimCoach.Coach;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class RetryPromptResourceTests
{
    [Fact]
    public void Retry_reminder_resolves_and_is_non_empty()
    {
        string text = PromptResources.ReadRetryReminder("v1");
        text.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Retry_reminder_resource_name_keeps_the_ru_infix()
    {
        // The .ru. infix must survive into the manifest name (LogicalName pin); a stripped name would 404.
        PromptResources.RetryReminderResourceName("v1")
            .Should().Be("SimCoach.Coach.Prompts.coach.retry.v1.ru.txt");
    }
}
