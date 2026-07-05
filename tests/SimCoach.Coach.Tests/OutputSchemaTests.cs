using System.Text.Json;
using FluentAssertions;
using SimCoach.Coach;
using SimCoach.Coach.Schema;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class OutputSchemaTests
{
    private static void AssertRequiredEqualsProperties(JsonElement schema)
    {
        var required = schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        var properties = schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        required.Should().BeEquivalentTo(properties);
    }

    [Fact]
    public void RealTime_action_id_enum_is_exactly_the_subset()
    {
        using var doc = JsonDocument.Parse(OutputSchema.RealTime(["wider_entry", "brake_later_by_meters"], allowAbstain: false));

        var enumValues = doc.RootElement
            .GetProperty("properties").GetProperty("action_id").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        enumValues.Should().Equal("wider_entry", "brake_later_by_meters");
    }

    [Fact]
    public void RealTime_puts_subset_first_in_action_id_enum()
    {
        using var doc = JsonDocument.Parse(OutputSchema.RealTime(["wider_entry"], allowAbstain: false));

        string? first = doc.RootElement
            .GetProperty("properties").GetProperty("action_id").GetProperty("enum")[0].GetString();

        first.Should().Be("wider_entry");
    }

    [Fact]
    public void RealTime_carries_no_length_constraints()
    {
        string schema = OutputSchema.RealTime(["wider_entry", "higher_min_speed"], allowAbstain: false);

        schema.Should().NotContain("maxLength").And.NotContain("minLength");
    }

    [Fact]
    public void RealTime_required_equals_property_keys()
    {
        using var doc = JsonDocument.Parse(OutputSchema.RealTime(["wider_entry"], allowAbstain: false));

        AssertRequiredEqualsProperties(doc.RootElement);
    }

    [Fact]
    public void RealTime_appends_none_sentinel_once_when_abstain_allowed()
    {
        using var doc = JsonDocument.Parse(OutputSchema.RealTime(["corner_catch_all"], allowAbstain: true));

        var enumValues = doc.RootElement
            .GetProperty("properties").GetProperty("action_id").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        enumValues.Should().Equal("corner_catch_all", OutputSchema.AbstainActionId);
        enumValues.Count(v => v == OutputSchema.AbstainActionId).Should().Be(1);
    }

    [Fact]
    public void RealTime_omits_none_sentinel_when_abstain_not_allowed()
    {
        using var doc = JsonDocument.Parse(OutputSchema.RealTime(["corner_catch_all"], allowAbstain: false));

        doc.RootElement
            .GetProperty("properties").GetProperty("action_id").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString())
            .Should().NotContain(OutputSchema.AbstainActionId);
    }

    [Fact]
    public void RealTime_required_is_unchanged_by_abstain_sentinel()
    {
        using var withAbstain = JsonDocument.Parse(OutputSchema.RealTime(["corner_catch_all"], allowAbstain: true));
        using var without = JsonDocument.Parse(OutputSchema.RealTime(["corner_catch_all"], allowAbstain: false));

        AssertRequiredEqualsProperties(withAbstain.RootElement);
        AssertRequiredEqualsProperties(without.RootElement);
        withAbstain.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("action_id", "phrase_ru");
    }

    [Fact]
    public void RealTime_adds_confidence_enum_and_required_when_requested()
    {
        using var doc = JsonDocument.Parse(
            OutputSchema.RealTime(["wider_entry"], allowAbstain: false, requestConfidence: true));
        JsonElement root = doc.RootElement;

        var enumValues = root
            .GetProperty("properties").GetProperty("confidence").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();
        enumValues.Should().Equal(OutputSchema.ConfidenceHigh, OutputSchema.ConfidenceLow);

        root.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("action_id", "phrase_ru", "confidence");
        root.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        // The strict invariant must hold when confidence is on.
        AssertRequiredEqualsProperties(root);
    }

    [Fact]
    public void RealTime_omits_confidence_when_not_requested()
    {
        using var doc = JsonDocument.Parse(
            OutputSchema.RealTime(["wider_entry"], allowAbstain: false, requestConfidence: false));
        JsonElement root = doc.RootElement;

        root.GetProperty("properties").TryGetProperty("confidence", out _).Should().BeFalse();
        root.GetProperty("required").EnumerateArray().Select(e => e.GetString())
            .Should().Equal("action_id", "phrase_ru");
        // The invariant still holds with confidence off.
        AssertRequiredEqualsProperties(root);
    }

    [Fact]
    public void RealTime_confidence_composes_with_abstain_and_keeps_the_invariant()
    {
        using var doc = JsonDocument.Parse(
            OutputSchema.RealTime(["corner_catch_all"], allowAbstain: true, requestConfidence: true));
        JsonElement root = doc.RootElement;

        // Abstain sentinel rides the action_id enum; confidence rides its own — required still == keys(properties).
        root.GetProperty("properties").GetProperty("action_id").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).Should().Contain(OutputSchema.AbstainActionId);
        AssertRequiredEqualsProperties(root);
    }

    [Fact]
    public void Debrief_bounds_top_losses_to_max_debrief_losses()
    {
        int cap = new CoachOptions().MaxDebriefLosses;

        using var doc = JsonDocument.Parse(OutputSchema.Debrief(cap));

        doc.RootElement.GetProperty("properties").GetProperty("top_losses")
            .GetProperty("maxItems").GetInt32().Should().Be(cap);
    }

    [Fact]
    public void Debrief_setup_hint_is_a_string_null_union()
    {
        using var doc = JsonDocument.Parse(OutputSchema.Debrief(5));

        var types = doc.RootElement.GetProperty("properties").GetProperty("setup_hint")
            .GetProperty("type").EnumerateArray().Select(e => e.GetString()).ToList();

        types.Should().Equal("string", "null");
    }

    [Fact]
    public void Debrief_required_equals_property_keys_at_both_levels()
    {
        using var doc = JsonDocument.Parse(OutputSchema.Debrief(5));

        AssertRequiredEqualsProperties(doc.RootElement);
        AssertRequiredEqualsProperties(
            doc.RootElement.GetProperty("properties").GetProperty("top_losses").GetProperty("items"));
    }

    [Fact]
    public void PhraseWordCount_counts_words_and_flags_overlong()
    {
        PhraseWordCount.Count("Шире вход в Eau Rouge").Should().Be(5);
        PhraseWordCount.Count("   ").Should().Be(0);
        PhraseWordCount.Count("один два три четыре пять шесть семь восемь девять")
            .Should().BeGreaterThan(new CoachOptions().InCornerMaxWords);
    }
}
