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

    [Fact]
    public void Retry_reason_resource_name_keeps_the_ru_infix()
    {
        PromptResources.RetryReasonResourceName("v1")
            .Should().Be("SimCoach.Coach.Prompts.coach.retry-reason.v1.ru.txt");
    }

    [Fact]
    public void Retry_reason_resource_resolves_and_is_non_empty()
    {
        PromptResources.ReadRetryReasons("v1").Should().NotBeEmpty();
    }

    [Theory]
    // Byte-identical guard: each validator reason (some with a dynamic suffix) still renders the exact RU line it
    // did when the RU text was inlined in RetryReasonRu — now sourced from the embedded coach.retry-reason resource.
    [InlineData("missing action_id/phrase_ru", "Причина отказа: нет обязательных полей action_id или phrase_ru")]
    [InlineData("action_id 'foo_bar' not in valid_actions", "Причина отказа: action_id не из разрешённого списка")]
    [InlineData("empty phrase_ru", "Причина отказа: пустое поле phrase_ru")]
    [InlineData("phrase_ru exceeds 12 words", "Причина отказа: фраза длиннее лимита слов")]
    [InlineData("missing top_losses/top_priority", "Причина отказа: нет обязательных полей top_losses или top_priority")]
    [InlineData("top_losses exceeds 3 items", "Причина отказа: слишком много элементов в top_losses")]
    [InlineData("empty top_priority", "Причина отказа: пустое поле top_priority")]
    [InlineData("debrief exceeds 60 words", "Причина отказа: разбор длиннее лимита слов")]
    [InlineData("malformed json at position 4", "Причина отказа: ответ не является валидным JSON")]
    [InlineData("SchemaViolation: unexpected token", "Причина отказа: ответ не соответствует схеме")]
    public void Retry_reason_line_renders_the_expected_russian(string reason, string expected)
    {
        IReadOnlyDictionary<string, string> reasons = PromptResources.ReadRetryReasons("v1");

        RetryReasonRu.Line(reasons, reason).Should().Be(expected);
    }

    [Fact]
    public void Retry_reason_line_echoes_an_unrecognised_reason_verbatim()
    {
        IReadOnlyDictionary<string, string> reasons = PromptResources.ReadRetryReasons("v1");

        RetryReasonRu.Line(reasons, "something the validator has never emitted")
            .Should().Be("Причина отказа: something the validator has never emitted");
    }
}
