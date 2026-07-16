using FluentAssertions;
using SimCoach.Coach.Actions;
using Xunit;

namespace SimCoach.Coach.Tests;

/// <summary>
/// M38 alien-regime review (MUST-FIX #5, OD7) — RU phrasing confirmation. The five line-deviation actions are
/// DIRECTIONAL ("плотнее" / "шире" / "ближе к апексу"), not magnitude-aware, so they read correctly against a
/// 2–4 m alien corridor exactly as they do against the driver's own median line: "move toward the reference
/// line". These lock the existing directional selection + phrasing at alien magnitudes so no drift ships (no
/// new .resx / registry phrase strings were added for the alien regime).
/// </summary>
public sealed class AlienRegimeGateTests
{
    private static readonly ActionRegistry _registry = ActionRegistry.Load();

    [Theory]
    [InlineData("entry_line_deviation_m", 3.0, "tighten_entry", "Плотнее вход в {corner}.")]
    [InlineData("entry_line_deviation_m", -3.0, "open_entry", "Шире заход в {corner}.")]
    [InlineData("exit_line_deviation_m", 3.0, "tighten_exit", "Плотнее выход из {corner}.")]
    [InlineData("exit_line_deviation_m", -3.0, "open_exit", "Раскрывай выход из {corner}, шире.")]
    public void A_directional_line_offset_selects_the_matching_action_and_phrase(
        string field, double value, string expectedId, string expectedPhrase)
    {
        DictionaryGoldView gold = CornerGold(new Dictionary<string, double> { [field] = value });

        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(gold, new CoachOptions());

        subset.Should().Contain(a => a.Id == expectedId);
        subset.Single(a => a.Id == expectedId).PhraseTemplateRu.Should().Be(
            expectedPhrase, "the directional alien-corridor phrasing is confirmed unchanged");
    }

    [Fact]
    public void A_wide_apex_line_with_a_lower_min_speed_voices_tighten_apex()
    {
        DictionaryGoldView gold = CornerGold(new Dictionary<string, double>
        {
            ["racing_line_deviation_m"] = 3.0,
            ["min_speed_diff_kmh"] = -5.0,
        });

        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(gold, new CoachOptions());

        subset.Should().Contain(a => a.Id == "tighten_apex");
        subset.Single(a => a.Id == "tighten_apex").PhraseTemplateRu.Should().Be("Ближе к апексу в {corner}.");
    }

    [Fact]
    public void An_apex_line_offset_without_a_speed_deficit_does_not_voice_tighten_apex()
    {
        // tighten_apex requires BOTH a wide line AND a lower min speed — a wide line that is already fast
        // enough is not a mistake to voice. Locks the second clause so the alien regime does not over-fire.
        DictionaryGoldView gold = CornerGold(new Dictionary<string, double>
        {
            ["racing_line_deviation_m"] = 3.0,
            ["min_speed_diff_kmh"] = 1.0,
        });

        _registry.ValidSubset(gold, new CoachOptions()).Select(a => a.Id).Should().NotContain("tighten_apex");
    }

    private static DictionaryGoldView CornerGold(IReadOnlyDictionary<string, double> numbers) => new(
        CoachCadence.Corner,
        hasReference: true,
        numbers,
        new Dictionary<string, bool> { ["off_track"] = false });
}
