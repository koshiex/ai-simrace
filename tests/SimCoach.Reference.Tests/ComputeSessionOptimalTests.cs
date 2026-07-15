using System.Text.Json;
using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Storage.Repositories;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

/// <summary>
/// M46 own-optimal delta routing through <see cref="ComputeSession"/>: TIME-only per-sector / per-lap deltas
/// against the persisted own-optimal, the current-session-aware session gap, the first-session fallback, and
/// the LINE/TIME separation invariant (the optimal never perturbs a corner's line/brake/throttle diffs).
/// </summary>
public sealed class ComputeSessionOptimalTests
{
    private const string SessionId = "20260601-120000-000";

    // The synthetic Spa triple every SyntheticSessionBuilder frame carries.
    private static readonly ReferenceTriple _triple = new("spa", "synthetic_gt3", "dry-warm");

    // Per-sector best durations deliberately BELOW every synthetic sector time (~670 ms each at 200 samples /
    // 2000 ms lap), so this session is slower than the optimal in every sector: merged == persisted, deltas and
    // deficits are positive, and the arithmetic is fully pinned.
    private static readonly int[] _optimalDurations = [300, 300, 300];
    private const int OptimalTargetMs = 900; // Σ _optimalDurations = the last cumulative boundary.

    private static void SeedOptimal(ComputeTestHarness harness) => harness.References.Upsert(new ReferenceRow
    {
        Id = Guid.NewGuid().ToString("N"),
        TrackId = _triple.TrackId,
        CarId = _triple.CarId,
        WeatherBucket = _triple.WeatherBucket,
        LapTimeMs = OptimalTargetMs,
        ParquetPath = null,
        CreatedAtUtc = DateTimeOffset.UnixEpoch,
        Kind = "optimal",
        OptimalSectorMs = JsonSerializer.Serialize(_optimalDurations),
    });

    [Fact]
    public async Task Optimal_sector_and_lap_deltas_are_measured_at_boundaries_against_the_persisted_optimal()
    {
        using var harness = new ComputeTestHarness();
        SeedOptimal(harness);
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        // Each crossing's optimal delta is exactly this sector's time minus the persisted per-sector duration
        // (all 300 ms). No mid-sector interpolation, no ResampledLap — pure index arithmetic.
        IReadOnlyList<SectorEvent> sectors = [.. events.OfType<SectorEvent>(DomainEventKind.Sector)];
        sectors.Should().NotBeEmpty();
        sectors.Should().OnlyContain(s => s.OptimalDeltaMs == s.SectorTimeMs - 300);
        sectors.Should().Contain(s => s.OptimalDeltaMs > 0, "every synthetic sector is slower than the 300 ms optimal");

        // Each lap's optimal delta is its time minus the optimal target (Σ best sectors = 900 ms).
        IReadOnlyList<LapEvent> laps = [.. events.OfType<LapEvent>(DomainEventKind.Lap)];
        laps.Should().NotBeEmpty();
        laps.Should().OnlyContain(l => l.OptimalDeltaMs == l.LapTimeMs - OptimalTargetMs);
    }

    [Fact]
    public async Task Session_gap_is_pb_minus_the_merged_optimal_with_a_positive_per_sector_deficit_vector()
    {
        using var harness = new ComputeTestHarness();
        SeedOptimal(harness);
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        SessionEvent session = events.OfType<SessionEvent>(DomainEventKind.Session).Single();
        // This session is slower than the optimal in every sector, so merged == persisted (Σ = 900 ms) and the
        // gap is the PB minus that. The deficit vector is one non-negative entry per sector.
        session.OptimalGapMs.Should().Be(session.PbTimeMs - OptimalTargetMs);
        session.OptimalGapMs.Should().BeGreaterThan(0);
        session.SectorOptimalGapMs.Should().HaveCount(3);
        session.SectorOptimalGapMs.Should().OnlyContain(d => d > 0);
    }

    [Fact]
    public async Task First_session_without_a_persisted_optimal_omits_the_optimal_gap_and_keeps_field_16()
    {
        using var harness = new ComputeTestHarness();
        // No optimal seeded — the first-ever session for the triple.
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        SessionEvent session = events.OfType<SessionEvent>(DomainEventKind.Session).Single();
        // Empty deficit vector is the "no persisted optimal" signal the Gold layer reads to fall back to
        // field 16; the within-session theoretical best still computes.
        session.SectorOptimalGapMs.Should().BeEmpty();
        session.OptimalGapMs.Should().Be(0);
        session.TheoreticalBestGapMs.Should().BeGreaterThanOrEqualTo(0);

        // Per-event optimal deltas stay at the proto default without a persisted optimal.
        events.OfType<SectorEvent>(DomainEventKind.Sector).Should().OnlyContain(s => s.OptimalDeltaMs == 0);
        events.OfType<LapEvent>(DomainEventKind.Lap).Should().OnlyContain(l => l.OptimalDeltaMs == 0);
    }

    [Fact]
    public async Task Optimal_feeds_time_deltas_only_and_never_perturbs_a_corner_line_or_pedal_diff()
    {
        // Differential LINE/TIME separation: the SAME frames with and without a persisted optimal must yield
        // byte-identical CornerEvents (delta, brake point, min-speed, throttle-resume, every line-deviation
        // field) and identical PB-relative sector deltas — the optimal only writes the *_optimal_* TIME fields.
        using var withoutHarness = new ComputeTestHarness();
        using var withHarness = new ComputeTestHarness();
        SeedOptimal(withHarness);

        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        IReadOnlyList<DomainEvent> without = await withoutHarness.RunAsync(frames, SessionId);
        IReadOnlyList<DomainEvent> with = await withHarness.RunAsync(frames, SessionId);

        // CornerEvents have no optimal field and must not shift at all — proto value equality proves the line
        // reference and every pedal/min-speed diff is untouched by the optimal.
        List<CornerEvent> withoutCorners = [.. without.OfType<CornerEvent>(DomainEventKind.Corner)];
        List<CornerEvent> withCorners = [.. with.OfType<CornerEvent>(DomainEventKind.Corner)];
        withCorners.Should().Equal(withoutCorners);

        // Sector events: the PB-relative delta is identical; only the optimal TIME field diverges.
        List<SectorEvent> withoutSectors = [.. without.OfType<SectorEvent>(DomainEventKind.Sector)];
        List<SectorEvent> withSectors = [.. with.OfType<SectorEvent>(DomainEventKind.Sector)];
        withSectors.Should().HaveCount(withoutSectors.Count);
        for (int i = 0; i < withSectors.Count; i++)
        {
            withSectors[i].DeltaMs.Should().Be(withoutSectors[i].DeltaMs, "the PB-relative delta is optimal-agnostic");
            withoutSectors[i].OptimalDeltaMs.Should().Be(0, "no optimal → no optimal delta");
            withSectors[i].OptimalDeltaMs.Should().NotBe(0, "the optimal writes only the TIME field");
        }
    }
}
