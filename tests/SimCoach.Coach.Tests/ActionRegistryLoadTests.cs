using System.Text;
using FluentAssertions;
using SimCoach.Coach.Actions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class ActionRegistryLoadTests
{
    private static ActionRegistry LoadJson(string json) =>
        ActionRegistry.LoadFrom(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    private static string Registry(string actionsBody) =>
        $$"""{ "schema_version": "actions/1", "actions": [ {{actionsBody}} ] }""";

    [Fact]
    public void Loads_embedded_registry_without_throwing()
    {
        Action act = () => ActionRegistry.Load();

        act.Should().NotThrow();
    }

    [Fact]
    public void Loads_the_authored_action_count()
    {
        var registry = ActionRegistry.Load();

        registry.Actions.Should().HaveCount(25);
    }

    [Fact]
    public void Authored_priorities_are_globally_unique()
    {
        var registry = ActionRegistry.Load();

        registry.Actions.Select(a => a.Priority).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Throws_on_wrong_schema_version()
    {
        const string json = """{ "schema_version": "actions/2", "actions": [] }""";

        Action act = () => LoadJson(json);

        act.Should().Throw<InvalidOperationException>().WithMessage("*schema_version*");
    }

    [Fact]
    public void Throws_when_a_when_field_is_unknown_for_the_cadence()
    {
        string json = Registry("""
            { "id": "x", "label_short": "x", "cadence": "corner",
              "priority": { "phase": "brake", "rank": 1 }, "requires_reference": false,
              "when": [ { "field": "not_a_field", "op": "lt", "value": 1 } ],
              "params": [], "phrase_template_ru": "тест" }
            """);

        Action act = () => LoadJson(json);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not_a_field*");
    }

    [Fact]
    public void Throws_when_a_param_from_field_is_unknown()
    {
        string json = Registry("""
            { "id": "x", "label_short": "x", "cadence": "corner",
              "priority": { "phase": "brake", "rank": 1 }, "requires_reference": false,
              "when": [], "params": [ { "name": "v", "from": "not_a_field" } ],
              "phrase_template_ru": "{v}" }
            """);

        Action act = () => LoadJson(json);

        act.Should().Throw<InvalidOperationException>().WithMessage("*not_a_field*");
    }

    [Fact]
    public void Throws_on_a_duplicate_action_id()
    {
        string json = Registry("""
            { "id": "dup", "label_short": "a", "cadence": "corner",
              "priority": { "phase": "brake", "rank": 1 }, "requires_reference": false,
              "when": [], "params": [], "phrase_template_ru": "a" },
            { "id": "dup", "label_short": "b", "cadence": "corner",
              "priority": { "phase": "brake", "rank": 2 }, "requires_reference": false,
              "when": [], "params": [], "phrase_template_ru": "b" }
            """);

        Action act = () => LoadJson(json);

        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate action id*");
    }

    [Fact]
    public void Throws_on_a_duplicate_priority()
    {
        string json = Registry("""
            { "id": "a", "label_short": "a", "cadence": "corner",
              "priority": { "phase": "brake", "rank": 5 }, "requires_reference": false,
              "when": [], "params": [], "phrase_template_ru": "a" },
            { "id": "b", "label_short": "b", "cadence": "corner",
              "priority": { "phase": "brake", "rank": 5 }, "requires_reference": false,
              "when": [], "params": [], "phrase_template_ru": "b" }
            """);

        Action act = () => LoadJson(json);

        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate priority*");
    }

    [Fact]
    public void Throws_when_a_template_placeholder_has_no_param()
    {
        string json = Registry("""
            { "id": "x", "label_short": "x", "cadence": "corner",
              "priority": { "phase": "brake", "rank": 1 }, "requires_reference": false,
              "when": [], "params": [], "phrase_template_ru": "В {corner}." }
            """);

        Action act = () => LoadJson(json);

        act.Should().Throw<InvalidOperationException>().WithMessage("*corner*");
    }

    [Fact]
    public void Throws_on_an_unknown_clause_op()
    {
        string json = Registry("""
            { "id": "x", "label_short": "x", "cadence": "corner",
              "priority": { "phase": "brake", "rank": 1 }, "requires_reference": false,
              "when": [ { "field": "delta_ms", "op": "between", "value": 1 } ],
              "params": [], "phrase_template_ru": "тест" }
            """);

        Action act = () => LoadJson(json);

        act.Should().Throw<InvalidOperationException>().WithMessage("*op*");
    }
}
