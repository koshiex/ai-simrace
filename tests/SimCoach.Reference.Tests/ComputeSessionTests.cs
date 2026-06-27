using System.Text.RegularExpressions;
using FluentAssertions;
using SimCoach.Contracts.V1;
using SimCoach.Storage.Repositories;
using SimCoach.TestKit;
using Xunit;

namespace SimCoach.Reference.Tests;

public sealed class ComputeSessionTests
{
    private const string SessionId = "20260601-120000-000";

    [Fact]
    public async Task Emits_lap_sector_corner_and_session_events_for_a_covered_track()
    {
        using var harness = new ComputeTestHarness();
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        // lapCount 4 → laps 2 and 3 are fully bounded (1 and 4 are partial, discarded).
        events.OfType<LapEvent>(DomainEventKind.Lap).Should().HaveCount(2);
        events.OfType<CornerEvent>(DomainEventKind.Corner).Should().NotBeEmpty();
        events.OfType<SectorEvent>(DomainEventKind.Sector).Should().NotBeEmpty();
        events.OfType<SessionEvent>(DomainEventKind.Session).Should().ContainSingle();

        SessionEvent session = events.OfType<SessionEvent>(DomainEventKind.Session).Single();
        session.LapCount.Should().Be(2);
        session.CleanLapCount.Should().Be(2);
        session.PbTimeMs.Should().BeGreaterThan(0);
        session.TrackId.Should().Be("spa");
    }

    [Fact]
    public async Task Corner_ids_are_track_scoped_tokens()
    {
        using var harness = new ComputeTestHarness();
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        events.OfType<CornerEvent>(DomainEventKind.Corner)
            .Should().OnlyContain(c => c.CornerId.StartsWith("spa", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Persists_a_lap_row_per_bounded_lap_and_marks_the_first_clean_lap_pb()
    {
        using var harness = new ComputeTestHarness();
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        IReadOnlyList<LapRow> laps = harness.Laps.GetBySession(SessionId);
        laps.Should().HaveCount(2);
        laps.Count(l => l.IsPb).Should().Be(1, "only the first clean lap beats the int.MaxValue session best");

        List<LapEvent> lapEvents = [.. events.OfType<LapEvent>(DomainEventKind.Lap)];
        lapEvents[0].IsPb.Should().BeTrue();
        lapEvents[1].IsPb.Should().BeFalse("the identical second lap does not beat the first");
    }

    [Fact]
    public async Task Pit_return_lap_counter_reset_writes_distinct_rows_without_dropping_a_lap()
    {
        // Reproduce issue #13: two stints whose sim lap_number restarts at the box (the second stint
        // re-issues 1, 2, 3…). Before the fix this collided on UNIQUE(session_id, lap_number) and
        // crashed the host. After the fix every emitted lap is renumbered to a unique, monotonic value,
        // so each LapEvent yields exactly one LapRow — nothing collides and nothing is dropped.
        using var harness = new ComputeTestHarness();
        IReadOnlyList<TelemetryFrame> frames = PitReturnSession();

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        int lapEventCount = events.OfType<LapEvent>(DomainEventKind.Lap).Count();
        lapEventCount.Should().BeGreaterThan(2, "the two stints together bound more laps than one stint");

        IReadOnlyList<LapRow> laps = harness.Laps.GetBySession(SessionId);
        laps.Should().HaveCount(lapEventCount, "every emitted lap is persisted — none lost to a collision");
        laps.Select(l => l.LapNumber).Should().OnlyHaveUniqueItems();
        laps.Select(l => l.LapNumber).Should().BeInAscendingOrder();
    }

    // Two back-to-back stints on the same session: the second restarts the sim lap counter (a pit
    // return), so its frames re-issue lap numbers already completed in the first stint. Timestamps are
    // continued past the first stint so lap times stay positive and the seam reads as a start-line wrap.
    private static IReadOnlyList<TelemetryFrame> PitReturnSession()
    {
        IReadOnlyList<TelemetryFrame> stint1 = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        DateTimeOffset seam = stint1[^1].T.ToDateTimeOffset() + TimeSpan.FromMilliseconds(10);
        IReadOnlyList<TelemetryFrame> stint2 =
            SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4, startUtc: seam);
        return [.. stint1, .. stint2];
    }

    [Fact]
    public async Task Establishes_a_reference_for_the_triple_on_the_first_clean_lap()
    {
        using var harness = new ComputeTestHarness();
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        ReferenceRow? reference = harness.References.GetByTriple("spa", "synthetic_gt3", "dry-warm");
        reference.Should().NotBeNull();
        File.Exists(reference!.ParquetPath).Should().BeTrue();

        // First completed lap had no reference yet (delta defaults to 0); the second deltas against it.
        List<LapEvent> lapEvents = [.. events.OfType<LapEvent>(DomainEventKind.Lap)];
        lapEvents[0].DeltaMs.Should().Be(0);
    }

    [Fact]
    public async Task Uncovered_track_has_no_corner_model()
    {
        var lengths = new FakeTrackLengths(("test_oval", 2000f));
        using var harness = new ComputeTestHarness(lengths);
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.TestOval, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        // No baked geometry for this track and no mid-session derive (ADR-0014) → corners are suppressed.
        harness.TrackModels.Get("test_oval").Source.Should().Be(TrackModelSource.None);
        events.OfType<CornerEvent>(DomainEventKind.Corner).Should().BeEmpty();
        events.OfType<LapEvent>(DomainEventKind.Lap).Should().HaveCount(2);
    }

    [Fact]
    public async Task Covered_track_uses_a_fixed_baked_model_across_laps()
    {
        // Baked geometry resolves once at session start and never changes mid-session (ADR-0014):
        // every lap emits the same stable positional corner ids and the source stays Baked.
        using var harness = new ComputeTestHarness();
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        IReadOnlyList<CornerEvent> corners = [.. events.OfType<CornerEvent>(DomainEventKind.Corner)];
        corners.Should().NotBeEmpty();
        corners.Should().OnlyContain(
            c => Regex.IsMatch(c.CornerId, @"^spa_t\d+$"),
            "baked corners carry stable positional ids; the model is fixed for the session");
        harness.TrackModels.Get("spa").Source.Should().Be(TrackModelSource.Baked);
    }

    [Fact]
    public async Task Completes_the_domain_fan_out_even_with_no_frames()
    {
        using var harness = new ComputeTestHarness();
        DomainEventSubscription sub = harness.DomainFanOut.Subscribe("solo");

        // A session that never starts (Complete without Accept) must still close the stream.
        var session = new ComputeSession(
            harness.DomainFanOut, harness.TrackModels, harness.Lookup, harness.ReferenceStore, harness.Laps,
            FakeTrackLengths.Spa(), new ComputeOptions(), Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
            new SimCoach.Pipeline.SessionIdentity("empty", DateTimeOffset.UnixEpoch));
        session.Complete();

        List<DomainEvent> received = [];
        await foreach (DomainEvent e in sub.ReadAllAsync())
        {
            received.Add(e);
        }

        received.Should().BeEmpty();
    }
}

/// <summary>Small typed projection helpers over the heterogeneous domain-event list.</summary>
internal static class DomainEventQuery
{
    public static IEnumerable<T> OfType<T>(this IEnumerable<DomainEvent> events, DomainEventKind kind)
        where T : class =>
        events.Where(e => e.Kind == kind).Select(e => (T)e.Payload);
}
