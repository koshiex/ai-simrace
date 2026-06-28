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
        using var doc = JsonDocument.Parse(OutputSchema.RealTime(["wider_entry", "brake_later_by_meters"]));

        var enumValues = doc.RootElement
            .GetProperty("properties").GetProperty("action_id").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString()).ToList();

        enumValues.Should().Equal("wider_entry", "brake_later_by_meters");
    }

    [Fact]
    public void RealTime_puts_subset_first_in_action_id_enum()
    {
        using var doc = JsonDocument.Parse(OutputSchema.RealTime(["wider_entry"]));

        string? first = doc.RootElement
            .GetProperty("properties").GetProperty("action_id").GetProperty("enum")[0].GetString();

        first.Should().Be("wider_entry");
    }

    [Fact]
    public void RealTime_carries_no_length_constraints()
    {
        string schema = OutputSchema.RealTime(["wider_entry", "higher_min_speed"]);

        schema.Should().NotContain("maxLength").And.NotContain("minLength");
    }

    [Fact]
    public void RealTime_required_equals_property_keys()
    {
        using var doc = JsonDocument.Parse(OutputSchema.RealTime(["wider_entry"]));

        AssertRequiredEqualsProperties(doc.RootElement);
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
