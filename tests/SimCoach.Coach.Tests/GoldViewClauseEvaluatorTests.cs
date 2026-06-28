using FluentAssertions;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class GoldViewClauseEvaluatorTests
{
    private static readonly ActionRegistry _registry = ActionRegistry.Load();
    private static readonly CoachOptions _options = new();

    private static IReadOnlyList<string> Subset(CornerEvent ev, bool hasReference)
    {
        IGoldView view = GoldView.For(GoldTestData.Builder().BuildCorner(ev, GoldTestData.Ctx(hasReference)));
        return [.. _registry.ValidSubset(view, _options).Select(a => a.Id)];
    }

    [Fact]
    public void Brake_point_diff_and_off_track_select_brake_later()
    {
        CornerEvent ev = GoldTestData.CornerNeutral();
        ev.BrakePointDiffM = -3.44f;

        Subset(ev, hasReference: true).Should().Equal("brake_later_by_meters");
    }

    [Fact]
    public void Derived_trail_brake_diff_selects_more_trail_brake()
    {
        CornerEvent ev = GoldTestData.CornerNeutral();
        ev.TrailBrakePctSelf = 0.2f;
        ev.TrailBrakePctRef = 0.4f;

        Subset(ev, hasReference: true).Should().Equal("more_trail_brake");
    }

    [Fact]
    public void Reference_required_actions_drop_without_a_reference()
    {
        CornerEvent ev = GoldTestData.CornerNeutral();
        ev.BrakePointDiffM = -3.44f;

        Subset(ev, hasReference: false).Should().BeEmpty();
    }

    [Theory]
    [InlineData(CoachCadence.Corner)]
    [InlineData(CoachCadence.Sector)]
    [InlineData(CoachCadence.Lap)]
    public void Every_clause_field_resolves_through_the_adapter(CoachCadence cadence)
    {
        GoldArtifactBuilder builder = GoldTestData.Builder();
        IGoldView view = cadence switch
        {
            CoachCadence.Corner => GoldView.For(builder.BuildCorner(GoldTestData.Corner(), GoldTestData.Ctx())),
            CoachCadence.Sector => GoldView.For(builder.BuildSector(GoldTestData.Sector(), GoldTestData.Ctx())),
            _ => GoldView.For(builder.BuildLap(GoldTestData.Lap(), GoldTestData.Ctx())),
        };

        foreach (string field in GoldFieldNames.For(cadence))
        {
            bool resolved = view.TryGetNumber(field, out _)
                || view.TryGetBool(field, out _)
                || view.TryGetString(field, out _);
            resolved.Should().BeTrue($"field '{field}' must resolve through the {cadence} adapter");
        }
    }
}
