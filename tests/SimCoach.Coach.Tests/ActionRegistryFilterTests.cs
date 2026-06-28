using FluentAssertions;
using SimCoach.Coach.Actions;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class ActionRegistryFilterTests
{
    private static readonly ActionRegistry _registry = ActionRegistry.Load();

    private static DictionaryGoldView CornerGold(
        bool hasReference,
        IReadOnlyDictionary<string, double> numbers,
        IReadOnlyDictionary<string, bool>? bools = null) =>
        new(
            CoachCadence.Corner,
            hasReference,
            numbers,
            bools ?? new Dictionary<string, bool> { ["off_track"] = false });

    private static DictionaryGoldView UndersteerGold(bool hasReference) =>
        CornerGold(
            hasReference,
            new Dictionary<string, double>
            {
                ["understeer_score"] = 0.8,
                ["min_speed_diff_kmh"] = -5.0,
                ["delta_ms"] = -200.0,
            });

    [Fact]
    public void Returns_empty_on_a_clean_corner()
    {
        DictionaryGoldView clean = CornerGold(
            hasReference: true,
            new Dictionary<string, double>
            {
                ["brake_point_diff_m"] = 0.0,
                ["min_speed_diff_kmh"] = 0.0,
                ["delta_ms"] = 0.0,
                ["understeer_score"] = 0.0,
                ["oversteer_score"] = 0.0,
                ["wheelspin_score"] = 0.0,
                ["brake_overlap_steer_pct"] = 0.0,
                ["steering_jitter"] = 0.0,
                ["trail_brake_diff_pct"] = 0.0,
                ["throttle_resume_diff_m"] = 0.0,
                ["racing_line_deviation_m"] = 0.0,
            });

        _registry.ValidSubset(clean, new CoachOptions()).Should().BeEmpty();
    }

    [Fact]
    public void Includes_only_matching_cadence_actions()
    {
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(UndersteerGold(hasReference: true), new CoachOptions());

        subset.Should().OnlyContain(a => a.Cadence == CoachCadence.Corner);
        subset.Should().NotBeEmpty();
    }

    [Fact]
    public void Excludes_reference_requiring_actions_when_no_reference()
    {
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(UndersteerGold(hasReference: false), new CoachOptions());

        subset.Should().OnlyContain(a => !a.RequiresReference);
        subset.Select(a => a.Id).Should().NotContain("wider_entry");
    }

    [Fact]
    public void Includes_reference_free_action_when_no_reference()
    {
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(UndersteerGold(hasReference: false), new CoachOptions());

        subset.Select(a => a.Id).Should().Contain("ease_understeer");
    }

    [Fact]
    public void Orders_by_priority_root_cause_first()
    {
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(UndersteerGold(hasReference: true), new CoachOptions());

        subset.Select(a => a.Id).Should().ContainInConsecutiveOrder("wider_entry", "ease_understeer", "higher_min_speed");
        subset.Should().BeInAscendingOrder(a => a.Priority);
    }

    [Fact]
    public void Caps_at_max_actions_in_menu()
    {
        var options = new CoachOptions { MaxActionsInMenu = 2 };

        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(UndersteerGold(hasReference: true), options);

        subset.Should().HaveCount(2);
        subset.Select(a => a.Id).Should().Equal("wider_entry", "ease_understeer");
    }
}
