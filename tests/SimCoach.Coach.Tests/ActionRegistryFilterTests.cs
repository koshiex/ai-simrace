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
    public void Fires_reference_free_action_on_a_cold_start_off_track_corner()
    {
        // M19: a lap-1 corner with no persisted reference but an off-track symptom must still yield a
        // reference-free action, where today the menu would be car-control-only/empty.
        var gold = new DictionaryGoldView(
            CoachCadence.Corner,
            hasReference: false,
            numbers: new Dictionary<string, double>(StringComparer.Ordinal),
            bools: new Dictionary<string, bool>(StringComparer.Ordinal) { ["off_track"] = true });

        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(gold, new CoachOptions());

        subset.Should().NotBeEmpty();
        subset.Select(a => a.Id).Should().Contain("ran_wide");
        subset.Should().OnlyContain(a => !a.RequiresReference);
        subset.Should().HaveCount(c => c <= new CoachOptions().MaxActionsInMenu);
    }

    [Fact]
    public void Fires_reference_free_absolute_trail_brake_action_on_a_cold_start_corner()
    {
        // M19: with no reference, an absolute near-zero trail-brake still yields a reference-free action —
        // but only once the driver actually braked (peak_brake_pct clears the had-braking gate).
        var gold = new DictionaryGoldView(
            CoachCadence.Corner,
            hasReference: false,
            numbers: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["trail_brake_pct_self"] = 0.0,
                ["peak_brake_pct"] = 0.8,
            },
            bools: new Dictionary<string, bool>(StringComparer.Ordinal) { ["off_track"] = false });

        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(gold, new CoachOptions());

        subset.Select(a => a.Id).Should().Contain("trail_brake_absent");
        subset.Should().OnlyContain(a => !a.RequiresReference);
    }

    [Fact]
    public void Does_not_fire_absolute_trail_brake_action_on_a_no_brake_corner()
    {
        // A flat/lift-only corner reports trail_brake_pct_self=0 with no braking; the had-braking gate
        // (peak_brake_pct below the brake floor) must keep trail_brake_absent silent instead of drawing a
        // "hold the brake longer" tip every lap.
        var gold = new DictionaryGoldView(
            CoachCadence.Corner,
            hasReference: false,
            numbers: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["trail_brake_pct_self"] = 0.0,
                ["peak_brake_pct"] = 0.0,
            },
            bools: new Dictionary<string, bool>(StringComparer.Ordinal) { ["off_track"] = false });

        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(gold, new CoachOptions());

        subset.Select(a => a.Id).Should().NotContain("trail_brake_absent");
    }

    [Fact]
    public void Orders_by_priority_root_cause_first()
    {
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(UndersteerGold(hasReference: true), new CoachOptions());

        subset.Select(a => a.Id).Should().ContainInConsecutiveOrder("wider_entry", "ease_understeer", "higher_min_speed");
        subset.Should().BeInAscendingOrder(a => a.Priority);
    }

    // A corner tripping the catch-all delta with a given reason and no specific-action symptom.
    private static DictionaryGoldView CatchAllGold(bool hasReference, string? reason) =>
        new(
            CoachCadence.Corner,
            hasReference,
            numbers: new Dictionary<string, double>(StringComparer.Ordinal) { ["delta_ms"] = 200.0 },
            bools: new Dictionary<string, bool>(StringComparer.Ordinal) { ["off_track"] = false },
            strings: reason is null
                ? new Dictionary<string, string>(StringComparer.Ordinal) { ["corner_name"] = "Eau Rouge" }
                : new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["corner_name"] = "Eau Rouge",
                    ["reason"] = reason,
                });

    [Fact]
    public void Corner_catch_all_glosses_the_reason_and_sets_no_chip()
    {
        // M21: the catch-all names the glossed cause instead of a bare millisecond count, and the reason
        // gloss is a string (not quantitative) so it never populates the overlay chip.
        CoachAction catchAll = _registry.Actions.Single(a => a.Id == "corner_catch_all");

        RenderedAction rendered = PhraseRenderer.Render(catchAll, CatchAllGold(hasReference: true, "late_throttle"), new CoachOptions());

        rendered.PhraseRu.Should().Be("В Eau Rouge теряешь: поздний газ на выходе.");
        rendered.PhraseRu.Should().NotContainAny("мс", "отклонение");
        rendered.RenderedParam.Should().BeEmpty();
    }

    [Theory]
    [InlineData("slower")]
    [InlineData(null)]
    public void Corner_catch_all_stays_silent_when_reason_is_empty_or_slower(string? reason)
    {
        // M21: a vague loss with no nameable cause emits nothing rather than a bare millisecond count.
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(CatchAllGold(hasReference: true, reason), new CoachOptions());

        subset.Select(a => a.Id).Should().NotContain("corner_catch_all");
    }

    [Fact]
    public void Corner_catch_all_fires_alone_when_reason_is_nameable_and_no_specific_action()
    {
        // M21: with a real reason and no specific symptom, the catch-all is the only passing action and
        // survives the same-family strip — the menu never empties.
        IReadOnlyList<CoachAction> subset =
            _registry.ValidSubset(CatchAllGold(hasReference: true, "late_throttle"), new CoachOptions());

        subset.Select(a => a.Id).Should().ContainSingle().Which.Should().Be("corner_catch_all");
    }

    [Fact]
    public void Corner_catch_all_is_stripped_when_a_specific_same_corner_action_passes()
    {
        // M21 targeted strip: an understeer symptom (specific, rank < CatchAllRank) survives, so the
        // undiscriminating catch-all is dropped from the menu even though its own clause holds.
        var gold = new DictionaryGoldView(
            CoachCadence.Corner,
            hasReference: true,
            numbers: new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["delta_ms"] = 200.0,
                ["understeer_score"] = 0.8,
                ["min_speed_diff_kmh"] = -5.0,
            },
            bools: new Dictionary<string, bool>(StringComparer.Ordinal) { ["off_track"] = false },
            strings: new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = "late_throttle" });

        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(gold, new CoachOptions());

        subset.Should().NotBeEmpty();
        subset.Select(a => a.Id).Should().NotContain("corner_catch_all");
        subset.Should().OnlyContain(a => a.Priority.Rank < new CoachOptions().CatchAllRank);
    }

    // A corner whose only non-neutral signal is a given brake-overlap fraction — every other field is 0,
    // so straighter_braking (brake_overlap_steer_pct > threshold) is the only action that can fire.
    private static DictionaryGoldView OverlapOnlyGold(double overlapPct) =>
        CornerGold(
            hasReference: true,
            new Dictionary<string, double>
            {
                ["brake_point_diff_m"] = 0.0,
                ["min_speed_diff_kmh"] = 0.0,
                ["delta_ms"] = 0.0,
                ["understeer_score"] = 0.0,
                ["oversteer_score"] = 0.0,
                ["wheelspin_score"] = 0.0,
                ["brake_overlap_steer_pct"] = overlapPct,
                ["steering_jitter"] = 0.0,
                ["trail_brake_diff_pct"] = 0.0,
                ["throttle_resume_diff_m"] = 0.0,
                ["racing_line_deviation_m"] = 0.0,
            });

    [Fact]
    public void Straighter_braking_stays_silent_below_the_recalibrated_threshold()
    {
        // M9: after phase-scoping, the Variante del Rettifilo braking-chicane false positive resolves to a
        // low turn-in→apex overlap (0.2), which sits below the recalibrated 0.5 registry threshold.
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(OverlapOnlyGold(0.2), new CoachOptions());

        subset.Select(a => a.Id).Should().NotContain(
            "straighter_braking", "a phase-scoped chicane overlap below the recalibrated threshold must not fire");
    }

    [Fact]
    public void Straighter_braking_fires_only_strictly_above_the_recalibrated_threshold()
    {
        // A genuine sustained brake-into-apex resolves to a high phase-scoped overlap (0.6 > 0.5) and
        // survives the filter — the recalibrated boundary is pinned here.
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(OverlapOnlyGold(0.6), new CoachOptions());

        subset.Select(a => a.Id).Should().Contain(
            "straighter_braking", "a genuine over-brake above the recalibrated threshold still fires");
    }

    [Fact]
    public void Straighter_braking_does_not_fire_exactly_at_the_recalibrated_threshold()
    {
        // The registry clause is strict `gt 0.5`, so an overlap of exactly 0.5 does NOT fire — pinning the
        // boundary against a `>=` regression.
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(OverlapOnlyGold(0.5), new CoachOptions());

        subset.Select(a => a.Id).Should().NotContain(
            "straighter_braking", "overlap exactly at the threshold is not strictly greater, so it stays silent");
    }

    [Fact]
    public void Straighter_braking_fires_just_above_the_recalibrated_threshold()
    {
        // The smallest discriminating step above 0.5 fires — the other half of the strict-greater boundary.
        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(OverlapOnlyGold(0.5001), new CoachOptions());

        subset.Select(a => a.Id).Should().Contain(
            "straighter_braking", "an overlap strictly above the threshold fires");
    }

    [Fact]
    public void Dirty_lap_yields_no_lap_cadence_limits_tip()
    {
        // OD4/C23: the spoken lap_dirty announcement is dropped — the driver already knows the lap was
        // invalidated. A lap-cadence gold view keyed off is_clean==false must emit no lap-cadence tip
        // (neither lap_dirty nor any other action gated on is_clean==false).
        // delta_ms held at 0 so nothing but an is_clean==false gate can fire — isolates the dropped tip.
        var dirtyLap = new DictionaryGoldView(
            CoachCadence.Lap,
            hasReference: true,
            numbers: new Dictionary<string, double>(StringComparer.Ordinal) { ["delta_ms"] = 0.0 },
            bools: new Dictionary<string, bool>(StringComparer.Ordinal) { ["is_clean"] = false });

        IReadOnlyList<CoachAction> subset = _registry.ValidSubset(dirtyLap, new CoachOptions());

        subset.Select(a => a.Id).Should().NotContain("lap_dirty");
        subset.Where(a => a.Cadence == CoachCadence.Lap).Should().BeEmpty();
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
