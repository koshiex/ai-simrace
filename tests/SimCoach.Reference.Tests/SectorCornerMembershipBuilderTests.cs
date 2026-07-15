using FluentAssertions;
using SimCoach.Contracts.V1;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class SectorCornerMembershipBuilderTests
{
    [Fact]
    public void Maps_each_observed_sector_to_the_corners_whose_apex_falls_in_its_range()
    {
        // Three observed sectors partition the lap; three baked corners sit one per sector. Membership is the
        // intersection of observed sector position ranges with baked apex positions — sectors stay
        // runtime-only (ADR-0010); the map is derived here, never persisted to the track model.
        var builder = new SectorCornerMembershipBuilder();
        builder.Observe(0, 0.00f, 0.33f);
        builder.Observe(1, 0.33f, 0.66f);
        builder.Observe(2, 0.66f, 1.00f);

        IReadOnlyList<SectorCornerMembership> membership = builder.Build(
        [
            CornerAt("t1", apex: 0.10f),
            CornerAt("t2", apex: 0.50f),
            CornerAt("t3", apex: 0.80f),
        ]);

        membership.Should().HaveCount(3);
        membership[0].SectorIndex.Should().Be(0);
        membership[0].CornerIds.Should().Equal("t1");
        membership[1].CornerIds.Should().Equal("t2");
        membership[2].CornerIds.Should().Equal("t3");
    }

    [Fact]
    public void Unions_observed_ranges_across_laps_and_orders_by_sector_index()
    {
        // The same sector observed on two laps at slightly different crossing positions unions to the widest
        // [min lo, max hi] range, and sectors emit in ascending index order regardless of observation order.
        // A corner whose apex lands only inside the widened span is still captured.
        var builder = new SectorCornerMembershipBuilder();
        builder.Observe(1, 0.40f, 0.60f);
        builder.Observe(0, 0.00f, 0.30f);
        builder.Observe(1, 0.35f, 0.66f); // lap 2: wider sector-1 crossing

        IReadOnlyList<SectorCornerMembership> membership = builder.Build(
        [
            CornerAt("s0", apex: 0.10f),
            CornerAt("edge", apex: 0.64f),
        ]);

        membership.Select(m => m.SectorIndex).Should().Equal(0, 1);
        // 0.64 falls inside the unioned [0.35, 0.66] sector-1 range (no `because` arg — Equal(params string[])
        // would otherwise read the reason as a second expected element).
        membership[1].CornerIds.Should().Equal("edge");
    }

    [Fact]
    public void Omits_a_cornerless_sector_from_the_membership()
    {
        // A straight sector with no baked apex inside its range emits no entry: the proto invariant is that
        // each emitted SectorCornerMembership maps to >=1 corner, so a corner-free sector is dropped rather
        // than surfaced with an empty corner list.
        var builder = new SectorCornerMembershipBuilder();
        builder.Observe(0, 0.00f, 0.50f);
        builder.Observe(1, 0.50f, 1.00f);

        IReadOnlyList<SectorCornerMembership> membership = builder.Build([CornerAt("t9", apex: 0.90f)]);

        membership.Should().ContainSingle();
        membership[0].SectorIndex.Should().Be(1);
        membership[0].CornerIds.Should().Equal("t9");
    }

    [Fact]
    public void Ignores_a_non_monotonic_crossing()
    {
        // A raw end < start observation is a wrap/teleport artefact — the caller folds a real wrap to 1.0, so
        // an inverted range is dropped rather than admitted (which would invert the membership test).
        var builder = new SectorCornerMembershipBuilder();
        builder.Observe(0, 0.80f, 0.10f);

        builder.Build([CornerAt("t1", apex: 0.05f)]).Should().BeEmpty();
    }

    private static Corner CornerAt(string id, float apex) => new()
    {
        Id = id,
        StartPosition = apex - 0.02f,
        ApexPosition = apex,
        EndPosition = apex + 0.02f,
    };
}
