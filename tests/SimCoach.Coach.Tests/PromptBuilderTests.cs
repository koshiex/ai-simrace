using System.Text.Json;
using FluentAssertions;
using SimCoach.Coach;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class PromptBuilderTests
{
    private static readonly ActionRegistry _registry = ActionRegistry.Load();
    private static readonly CoachOptions _coach = new();

    private static PromptBuilder NewBuilder() => new(_coach, new PromptOptions());

    private static (GoldArtifact<GoldCornerEvent> Gold, IReadOnlyList<CoachAction> Subset) CornerCase()
    {
        GoldArtifact<GoldCornerEvent> gold = GoldTestData.Builder().BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx());
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(GoldView.For(gold), _coach);
        return (gold, subset);
    }

    [Fact]
    public void Corner_builds_a_real_time_request_with_injected_menu()
    {
        (GoldArtifact<GoldCornerEvent> gold, IReadOnlyList<CoachAction> subset) = CornerCase();
        subset.Should().NotBeEmpty();

        LlmRequest request = NewBuilder().Build(gold, CoachCadence.Corner, subset);

        request.RouteKey.Should().Be("corner");
        request.SchemaName.Should().Be(SimCoach.Coach.Schema.OutputSchema.RealTimeSchemaName);
        request.SystemPrompt.Should().Contain("action_id");

        using var user = JsonDocument.Parse(request.UserPrompt);
        user.RootElement.GetProperty("valid_actions").GetArrayLength().Should().Be(subset.Count);
        user.RootElement.GetProperty("valid_actions")[0].GetProperty("id").GetString().Should().Be(subset[0].Id);
        user.RootElement.GetProperty("valid_actions")[0].GetProperty("hint").GetString().Should().Be(subset[0].HintEn);
        user.RootElement.GetProperty("valid_actions")[0].GetProperty("hint_ru").GetString().Should().Be(subset[0].HintRu);
        user.RootElement.GetProperty("phrase_limits").GetProperty("max_words").GetInt32()
            .Should().Be(_coach.InCornerMaxWords);

        using var schema = JsonDocument.Parse(request.JsonSchema);
        schema.RootElement.GetProperty("properties").GetProperty("action_id").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString())
            .Should().Equal(subset.Select(a => a.Id));
    }

    [Fact]
    public void Corner_weak_catch_all_lead_carries_the_none_sentinel()
    {
        (GoldArtifact<GoldCornerEvent> gold, _) = CornerCase();
        var catchAll = new CoachAction(
            "corner_catch_all", "corner_loss", CoachCadence.Corner,
            new CoachPriority(CoachPhase.Exit, _coach.CatchAllRank), RequiresReference: true,
            When: [], Params: [], "В {corner} отклонение около {loss}.", "notable corner time loss");

        LlmRequest request = NewBuilder().Build(gold, CoachCadence.Corner, [catchAll]);

        using var schema = JsonDocument.Parse(request.JsonSchema);
        schema.RootElement.GetProperty("properties").GetProperty("action_id").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString())
            .Should().Contain(SimCoach.Coach.Schema.OutputSchema.AbstainActionId);
        request.SystemPrompt.Should().Contain("none"); // the RU abstain guidance is appended
    }

    [Fact]
    public void Corner_specific_lead_does_not_carry_the_none_sentinel()
    {
        (GoldArtifact<GoldCornerEvent> gold, IReadOnlyList<CoachAction> subset) = CornerCase();
        subset[0].Priority.Rank.Should().BeLessThan(_coach.CatchAllRank); // a real specific action leads

        LlmRequest request = NewBuilder().Build(gold, CoachCadence.Corner, subset);

        using var schema = JsonDocument.Parse(request.JsonSchema);
        schema.RootElement.GetProperty("properties").GetProperty("action_id").GetProperty("enum")
            .EnumerateArray().Select(e => e.GetString())
            .Should().NotContain(SimCoach.Coach.Schema.OutputSchema.AbstainActionId);
    }

    [Fact]
    public void Session_builds_a_debrief_request_with_no_menu()
    {
        GoldArtifact<GoldSessionPayload> gold = GoldTestData.Builder().BuildSession(GoldTestData.Session(), GoldTestData.Ctx());

        LlmRequest request = NewBuilder().Build(gold, CoachCadence.Session, []);

        request.RouteKey.Should().Be("debrief");
        request.SchemaName.Should().Be(SimCoach.Coach.Schema.OutputSchema.DebriefSchemaName);
        request.SystemPrompt.Should().Contain("top_losses");
        request.JsonSchema.Should().Contain("top_losses");

        using var user = JsonDocument.Parse(request.UserPrompt);
        user.RootElement.TryGetProperty("valid_actions", out _).Should().BeFalse();
        user.RootElement.GetProperty("phrase_limits").GetProperty("max_words").GetInt32()
            .Should().Be(_coach.DebriefMaxWords);
    }

    [Fact]
    public void Empty_real_time_subset_throws()
    {
        (GoldArtifact<GoldCornerEvent> gold, _) = CornerCase();

        Action act = () => NewBuilder().Build(gold, CoachCadence.Corner, []);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Strategy_cadence_throws_a_clear_error()
    {
        (GoldArtifact<GoldCornerEvent> gold, IReadOnlyList<CoachAction> subset) = CornerCase();

        Action act = () => NewBuilder().Build(gold, CoachCadence.Strategy, subset);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Strategy*");
    }

    [Fact]
    public void Override_path_replaces_the_system_prompt()
    {
        string temp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(temp, "OVERRIDE_MARKER_SYSTEM");
            var options = new PromptOptions
            {
                Cadences = new Dictionary<CoachCadence, PromptSelection>
                {
                    [CoachCadence.Corner] = new PromptSelection(OverridePath: temp),
                    [CoachCadence.Sector] = new PromptSelection(),
                    [CoachCadence.Lap] = new PromptSelection(),
                    [CoachCadence.Session] = new PromptSelection(),
                },
            };
            (GoldArtifact<GoldCornerEvent> gold, IReadOnlyList<CoachAction> subset) = CornerCase();

            LlmRequest request = new PromptBuilder(_coach, options).Build(gold, CoachCadence.Corner, subset);

            request.SystemPrompt.Should().Contain("OVERRIDE_MARKER_SYSTEM");
            request.SystemPrompt.Should().NotContain("инженер-наставник");
        }
        finally
        {
            File.Delete(temp);
        }
    }

    [Fact]
    public void Few_shots_round_trip_as_request_response_pairs()
    {
        FewShotDocument document = PromptResources.ReadFewShots("v1");

        document.Examples.Should().NotBeNullOrEmpty();
        document.Examples!.Should().OnlyContain(e =>
            e.User.ValueKind != JsonValueKind.Undefined && e.Assistant.ValueKind != JsonValueKind.Undefined);

        FewShotExample corner = document.Examples!.First(e => e.Label == "corner-positive");
        corner.Assistant.GetProperty("action_id").GetString().Should().Be("wider_entry");
    }
}
