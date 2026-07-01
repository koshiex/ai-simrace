using FluentAssertions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class TipValidatorTests
{
    private static readonly string[] _subset = ["wider_entry", "brake_later_by_meters"];

    [Fact]
    public void Realtime_valid_returns_action_and_phrase()
    {
        bool ok = TipValidator.TryValidateRealtime(
            """{"action_id":"wider_entry","phrase_ru":"Шире вход в поворот."}""",
            _subset, maxWords: 8, out string action, out string phrase, out _);

        ok.Should().BeTrue();
        action.Should().Be("wider_entry");
        phrase.Should().Be("Шире вход в поворот.");
    }

    [Fact]
    public void Realtime_action_not_in_subset_fails()
    {
        bool ok = TipValidator.TryValidateRealtime(
            """{"action_id":"tighten_apex","phrase_ru":"Позже апекс."}""",
            _subset, maxWords: 8, out _, out _, out string failure);

        ok.Should().BeFalse();
        failure.Should().Contain("not in subset");
    }

    [Fact]
    public void Realtime_over_word_limit_fails()
    {
        bool ok = TipValidator.TryValidateRealtime(
            """{"action_id":"wider_entry","phrase_ru":"раз два три четыре пять"}""",
            _subset, maxWords: 3, out _, out _, out string failure);

        ok.Should().BeFalse();
        failure.Should().Contain("words");
    }

    [Fact]
    public void Realtime_empty_phrase_fails()
    {
        bool ok = TipValidator.TryValidateRealtime(
            """{"action_id":"wider_entry","phrase_ru":"  "}""",
            _subset, maxWords: 8, out _, out _, out string failure);

        ok.Should().BeFalse();
        failure.Should().Contain("empty");
    }

    [Fact]
    public void Realtime_malformed_json_fails()
    {
        bool ok = TipValidator.TryValidateRealtime(
            "{not json", _subset, maxWords: 8, out _, out _, out string failure);

        ok.Should().BeFalse();
        failure.Should().Contain("malformed");
    }

    [Fact]
    public void Debrief_valid_returns_priority()
    {
        bool ok = TipValidator.TryValidateDebrief(
            """{"top_losses":[{"corner":"Spa","ms":300,"why":"поздний газ"}],"top_priority":"Работай над выходами.","setup_hint":null}""",
            maxLosses: 5, maxWords: 200, out string priority, out _);

        ok.Should().BeTrue();
        priority.Should().Be("Работай над выходами.");
    }

    [Fact]
    public void Debrief_too_many_losses_fails()
    {
        string oneLoss = """{"corner":"C","ms":100,"why":"x"}""";
        string losses = string.Join(",", Enumerable.Repeat(oneLoss, 6));

        bool ok = TipValidator.TryValidateDebrief(
            $$"""{"top_losses":[{{losses}}],"top_priority":"p","setup_hint":null}""",
            maxLosses: 5, maxWords: 200, out _, out string failure);

        ok.Should().BeFalse();
        failure.Should().Contain("top_losses exceeds");
    }

    [Fact]
    public void Debrief_empty_priority_fails()
    {
        bool ok = TipValidator.TryValidateDebrief(
            """{"top_losses":[],"top_priority":"","setup_hint":null}""",
            maxLosses: 5, maxWords: 200, out _, out string failure);

        ok.Should().BeFalse();
        failure.Should().Contain("empty top_priority");
    }

    [Fact]
    public void Debrief_over_aggregate_word_limit_fails()
    {
        bool ok = TipValidator.TryValidateDebrief(
            """{"top_losses":[{"corner":"Spa","ms":300,"why":"раз два три"}],"top_priority":"четыре пять","setup_hint":null}""",
            maxLosses: 5, maxWords: 4, out _, out string failure);

        ok.Should().BeFalse();
        failure.Should().Contain("exceeds");
    }
}
