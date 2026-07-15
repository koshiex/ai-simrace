using System.Text.RegularExpressions;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
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
    public async Task Tip_quality_kernel_outputs_flow_through_to_events()
    {
        using var harness = new ComputeTestHarness();
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        // B1 self-derived corner fields are wired kernel → CornerEvent (not left at all-zero).
        IReadOnlyList<CornerEvent> corners = [.. events.OfType<CornerEvent>(DomainEventKind.Corner)];
        corners.Should().Contain(c => c.WheelspinScore > 0f);
        corners.Should().Contain(c => c.SteeringJitter > 0f);
        corners.Should().Contain(c => c.BrakeOverlapSteerPct > 0f);
        // Reference-lap corners resolve a concrete reason ("" only on the first, reference-free lap).
        corners.Should().Contain(c => c.Reason.Length > 0);

        // Lap-cadence thermal summary is wired kernel → LapEvent.
        IReadOnlyList<LapEvent> laps = [.. events.OfType<LapEvent>(DomainEventKind.Lap)];
        laps.Should().OnlyContain(l => l.Thermal != null);
        laps.Should().Contain(l => l.Thermal.MaxTyreTempC > 0f && l.Thermal.MaxBrakeTempC > 0f);
    }

    [Fact]
    public async Task Avg_fuel_per_lap_excludes_pit_laps()
    {
        using var harness = new ComputeTestHarness();
        // lapCount 4 → bounded laps are 2 and 3; mark lap 3 as a pit lap (skewed fuel = 0).
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(
            SyntheticTracks.Spa, lapCount: 4, pitLaps: new HashSet<int> { 3 });

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        SessionEvent session = events.OfType<SessionEvent>(DomainEventKind.Session).Single();
        // Only racing lap 2 (fuel 2.5) counts; the pit lap's 0 must not drag the mean to 1.25.
        session.AvgFuelPerLapL.Should().BeApproximately(2.5f, 1e-4f);
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
    public async Task Out_and_in_lap_frames_contribute_no_corner_or_sector_events()
    {
        // lapCount 4 → bounded laps 2 and 3. Marking lap 2 as a pit (out/in-lap) lap must remove exactly
        // its corner and sector contributions from the stream and the session aggregates; the flying laps
        // are untouched. (Synthetic pit laps carry clean-lap times, so the proof is the count delta, not a
        // magnitude — the real 66535 ms out-lap sector can no longer average into sector_avg_delta.)
        using var cleanHarness = new ComputeTestHarness();
        using var pitHarness = new ComputeTestHarness();

        IReadOnlyList<DomainEvent> cleanEvents = await cleanHarness.RunAsync(
            SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4), SessionId);
        IReadOnlyList<DomainEvent> pitEvents = await pitHarness.RunAsync(
            SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4, pitLaps: new HashSet<int> { 2 }), SessionId);

        int cleanCorners = cleanEvents.OfType<CornerEvent>(DomainEventKind.Corner).Count();
        int cleanSectors = cleanEvents.OfType<SectorEvent>(DomainEventKind.Sector).Count();
        pitEvents.OfType<CornerEvent>(DomainEventKind.Corner).Should().HaveCount(
            cleanCorners - SyntheticTracks.Spa.Corners.Count, "lap 2's corners are all suppressed");
        pitEvents.OfType<SectorEvent>(DomainEventKind.Sector).Should().HaveCount(
            cleanSectors - SyntheticTracks.Spa.SectorCount, "lap 2's sector crossings are all suppressed");

        // HandleLap is NOT gated: the pit lap still completes and counts as a lap.
        pitEvents.OfType<LapEvent>(DomainEventKind.Lap).Should().HaveCount(2);
    }

    [Fact]
    public async Task Track_limits_invalid_flying_lap_still_emits_corner_and_sector_tips()
    {
        // NEW M1 emission contract (was: a single is_valid_lap=false frame latched the whole lap silent).
        // A track-limits excursion no longer silences the rest of the lap's live coaching: the invalid
        // flying lap now emits the SAME corner + sector tips as an all-clean baseline. Only its use as an
        // aggregate/reference input is blocked (that half is pinned by the next test). The excursion frame
        // is placed before the first corner so a naive per-frame gate would suppress nothing — this proves
        // emission is decoupled from the accumulation latch, not merely that the latch stopped tripping.
        using var cleanHarness = new ComputeTestHarness();
        using var dirtyHarness = new ComputeTestHarness();

        IReadOnlyList<TelemetryFrame> clean = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        IReadOnlyList<TelemetryFrame> dirty = WithSingleInvalidFrame(clean, lapNumber: 3, atLeastPos: 0.02f);

        IReadOnlyList<DomainEvent> cleanEvents = await cleanHarness.RunAsync(clean, SessionId);
        IReadOnlyList<DomainEvent> dirtyEvents = await dirtyHarness.RunAsync(dirty, SessionId);

        // Corner + sector emission is now identical to the all-clean baseline — lap 3's excursion is coached
        // live exactly like the clean laps, not silenced.
        dirtyEvents.OfType<CornerEvent>(DomainEventKind.Corner).Should().HaveCount(
            cleanEvents.OfType<CornerEvent>(DomainEventKind.Corner).Count(),
            "an invalid flying lap still emits every corner tip; only its aggregate contribution is gated");
        dirtyEvents.OfType<SectorEvent>(DomainEventKind.Sector).Should().HaveCount(
            cleanEvents.OfType<SectorEvent>(DomainEventKind.Sector).Count(),
            "an invalid flying lap still emits every sector tip");

        // Content pin (the emission-scoped attribution defect): a STRETCHED invalid lap carries a real,
        // in-budget >100 ms S3 loss at t03. Its live SectorEvent.TopLosses must name that corner even
        // though the accumulation-gated aggregate stays empty. Sourced from _lapLosses (empty on an
        // invalid lap) TopLosses was empty and the Coach rendered a dangling "главное — ." phrase; sourced
        // from the emission-scoped _emitLosses it is well-formed. This leg FAILS before the fix.
        IReadOnlyList<TelemetryFrame> stretchedInvalid = WithSingleInvalidFrame(
            StretchBand(clean, lapNumber: 3, from: 0.78f, to: 0.90f, extraMs: 1800),
            lapNumber: 3, atLeastPos: 0.02f);

        using var contentHarness = new ComputeTestHarness();
        IReadOnlyList<DomainEvent> contentEvents = await contentHarness.RunAsync(stretchedInvalid, SessionId);

        List<SectorEvent> attributed =
            [.. contentEvents.OfType<SectorEvent>(DomainEventKind.Sector).Where(s => s.TopLosses.Any(l => l.CornerId == "spa_t03"))];
        attributed.Should().NotBeEmpty(
            "the invalid flying lap's live sector tip carries its corner attribution from the emission-scoped buffer");
        CornerLoss top = attributed.SelectMany(s => s.TopLosses).First(l => l.CornerId == "spa_t03");
        top.CornerId.Should().NotBeNullOrEmpty("the sector tip's top corner is populated, not a dangling phrase");
        top.DeltaMs.Should().BeGreaterThan(0, "the attributed loss carries its real, non-zero delta");
    }

    [Fact]
    public async Task Track_limits_invalid_flying_lap_is_coached_live_but_never_feeds_the_session_aggregate()
    {
        // NEW M1 accumulation contract: the invalid flying lap emits its live t03 loss tip (with the real,
        // non-zero delta) yet that loss must NEVER reach the session AggregatedLosses. Control lap 3 is a
        // VALID stretched lap whose t03 loss DOES aggregate; the treatment marks the SAME lap invalid with
        // one track-limits frame, after which the loss is coached but not aggregated. Deleting the
        // _lapPoisoned accumulation gate (accumulating on every emittable lap) fails the NotContain leg.
        IReadOnlyList<TelemetryFrame> baseFrames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        IReadOnlyList<TelemetryFrame> stretched = StretchBand(baseFrames, lapNumber: 3, from: 0.78f, to: 0.90f, extraMs: 1800);
        IReadOnlyList<TelemetryFrame> invalid = WithSingleInvalidFrame(stretched, lapNumber: 3, atLeastPos: 0.02f);

        using var validHarness = new ComputeTestHarness();
        using var invalidHarness = new ComputeTestHarness();
        IReadOnlyList<DomainEvent> validEvents = await validHarness.RunAsync(stretched, SessionId);
        IReadOnlyList<DomainEvent> invalidEvents = await invalidHarness.RunAsync(invalid, SessionId);

        // Control: the valid stretched lap's t03 loss reaches the session aggregate.
        validEvents.OfType<SessionEvent>(DomainEventKind.Session).Single()
            .AggregatedLosses.Should().Contain(l => l.CornerId == "spa_t03", "a valid lap's t03 loss aggregates");

        // Treatment: the SAME loss on the invalidated lap is still voiced live as a corner tip …
        invalidEvents.OfType<CornerEvent>(DomainEventKind.Corner).Should().Contain(
            c => c.CornerId == "spa_t03" && c.DeltaMs > 0,
            "the invalid flying lap is still coached corner-by-corner with its real loss");
        // … but never contaminates the session aggregate.
        invalidEvents.OfType<SessionEvent>(DomainEventKind.Session).Single()
            .AggregatedLosses.Should().NotContain(l => l.CornerId == "spa_t03", "an invalid lap must not skew the aggregate");
    }

    [Fact]
    public async Task Session_of_only_out_and_in_laps_emits_no_corner_or_sector_events()
    {
        // The lap_count==0 pathology (sessions 162041/165856): every lap is an out/in/pit lap. No corner
        // or sector event may fire and the session aggregates must be empty — yet the bounded laps still
        // complete (segmentation is position-based, independent of the pit flag).
        using var harness = new ComputeTestHarness();
        IReadOnlyList<TelemetryFrame> frames = SyntheticSessionBuilder.Build(
            SyntheticTracks.Spa, lapCount: 4, pitLaps: new HashSet<int> { 1, 2, 3, 4 });

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        events.OfType<CornerEvent>(DomainEventKind.Corner).Should().BeEmpty();
        events.OfType<SectorEvent>(DomainEventKind.Sector).Should().BeEmpty();
        SessionEvent session = events.OfType<SessionEvent>(DomainEventKind.Session).Single();
        session.AggregatedLosses.Should().BeEmpty();
        session.SectorAvgDeltaMs.Should().BeEmpty();
        events.OfType<LapEvent>(DomainEventKind.Lap).Should().HaveCount(2);
    }

    [Fact]
    public async Task Late_pit_dive_keeps_already_emitted_corners_but_suppresses_the_tail()
    {
        // Pins the frame-level latch boundary (Q1): a lap that emits valid corners and THEN dives into the
        // pit at the very end keeps its already-published corners (the latch cannot un-emit), while the
        // poisoned tail — here lap 3's final sector crossing at the lap boundary — is suppressed.
        using var cleanHarness = new ComputeTestHarness();
        using var lateHarness = new ComputeTestHarness();

        IReadOnlyList<TelemetryFrame> clean = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        // The last corner fires just past its end (0.90 → the 0.905 frame); entering the pit only after
        // that (0.92) leaves every corner intact while still poisoning the lap's final-sector tail.
        IReadOnlyList<TelemetryFrame> late = WithPitTail(clean, lapNumber: 3, fromPos: 0.92f);

        IReadOnlyList<DomainEvent> cleanEvents = await cleanHarness.RunAsync(clean, SessionId);
        IReadOnlyList<DomainEvent> lateEvents = await lateHarness.RunAsync(late, SessionId);

        // Early corners survive: the latch does not un-emit what was already published.
        lateEvents.OfType<CornerEvent>(DomainEventKind.Corner).Should().HaveCount(
            cleanEvents.OfType<CornerEvent>(DomainEventKind.Corner).Count());
        // Only the poisoned tail is lost: lap 3's boundary (final-sector) crossing is suppressed.
        lateEvents.OfType<SectorEvent>(DomainEventKind.Sector).Should().HaveCount(
            cleanEvents.OfType<SectorEvent>(DomainEventKind.Sector).Count() - 1);
    }

    [Fact]
    public async Task M3_tier_a_neutralises_an_over_ceiling_corner_delta_and_drops_it_from_losses()
    {
        // Pins the EmitCorner Tier-A wire-point (ComputeSession :221). Lap 2 is the reference; lap 3
        // carries a genuine over-ceiling reference-relative delta on Curva Grande (t03). With the wired
        // default ceiling the published delta is zeroed and the corner never reaches top-losses; with the
        // guard disabled the same frames leak the raw delta — the differential proves the guard is live,
        // not that the delta was never there.
        IReadOnlyList<TelemetryFrame> baseFrames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        IReadOnlyList<TelemetryFrame> frames = StretchBand(baseFrames, lapNumber: 3, from: 0.78f, to: 0.90f, extraMs: 3000);

        using var guarded = new ComputeTestHarness();
        using var disabled = new ComputeTestHarness();
        IReadOnlyList<DomainEvent> guardedEvents = await guarded.RunAsync(frames, SessionId);
        IReadOnlyList<DomainEvent> disabledEvents = await disabled.RunAsync(frames, SessionId, Permissive());

        int ceiling = new ComputeOptions().MaxPlausibleCornerLossMs;

        // Guard on: every published t03 delta collapses to 0 and t03 is absent from every losses surface.
        IReadOnlyList<CornerEvent> guardedT03 =
            [.. guardedEvents.OfType<CornerEvent>(DomainEventKind.Corner).Where(c => c.CornerId == "monza_t03" || c.CornerId == "spa_t03")];
        guardedT03.Should().OnlyContain(c => c.DeltaMs == 0, "the over-ceiling delta is neutralised before phrasing");
        List<LapEvent> guardedLaps = [.. guardedEvents.OfType<LapEvent>(DomainEventKind.Lap)];
        guardedLaps[1].TopLosses.Should().NotContain(l => l.CornerId == "spa_t03");
        guardedEvents.OfType<SessionEvent>(DomainEventKind.Session).Single()
            .AggregatedLosses.Should().NotContain(l => l.CornerId == "spa_t03");

        // Guard off: the same frames leak the raw over-ceiling delta straight into the losses surfaces.
        disabledEvents.OfType<CornerEvent>(DomainEventKind.Corner)
            .Where(c => c.CornerId == "spa_t03").Max(c => c.DeltaMs).Should().BeGreaterThan(ceiling);
        disabledEvents.OfType<SessionEvent>(DomainEventKind.Session).Single()
            .AggregatedLosses.Should().Contain(l => l.CornerId == "spa_t03");
        List<LapEvent> disabledLaps = [.. disabledEvents.OfType<LapEvent>(DomainEventKind.Lap)];
        disabledLaps[1].TopLosses.Should().Contain(l => l.CornerId == "spa_t03");
    }

    [Fact]
    public async Task M3_tier_a_neutralises_an_over_ceiling_sector_crossing_but_keeps_positional_indexing()
    {
        // Pins the EmitSector Tier-A wire-point (ComputeSession :260). Lap 3's final sector carries an
        // over-ceiling per-crossing delta while its first sector carries a small, in-budget one. The
        // poisoned crossing is neutralised to 0, the clean neighbour survives, and sector_avg_delta_ms
        // stays a 3-element positional vector (a dropped element would mis-index the debrief).
        IReadOnlyList<TelemetryFrame> baseFrames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        IReadOnlyList<TelemetryFrame> poisoned = StretchBand(baseFrames, lapNumber: 3, from: 0.667f, to: 1.0f, extraMs: 11000);
        IReadOnlyList<TelemetryFrame> frames = StretchBand(poisoned, lapNumber: 3, from: 0f, to: 0.10f, extraMs: 500);

        using var guarded = new ComputeTestHarness();
        using var disabled = new ComputeTestHarness();
        IReadOnlyList<DomainEvent> guardedEvents = await guarded.RunAsync(frames, SessionId);
        IReadOnlyList<DomainEvent> disabledEvents = await disabled.RunAsync(frames, SessionId, Permissive());

        int ceiling = new ComputeOptions().MaxPlausibleSectorLossMs;

        // Guard on: no per-crossing delta survives above the ceiling; the clean neighbour crossing does.
        IReadOnlyList<SectorEvent> guardedSectors = [.. guardedEvents.OfType<SectorEvent>(DomainEventKind.Sector)];
        guardedSectors.Max(s => s.DeltaMs).Should().BeLessThan(ceiling, "the over-ceiling crossing is zeroed");
        guardedSectors.Max(s => s.DeltaMs).Should().BeGreaterThan(0, "the small in-budget crossing is untouched");

        SessionEvent guardedSession = guardedEvents.OfType<SessionEvent>(DomainEventKind.Session).Single();
        guardedSession.SectorAvgDeltaMs.Should().HaveCount(3, "the aggregate stays a positional per-sector vector");
        guardedSession.SectorAvgDeltaMs.Should().Contain(0, "the poisoned sector is neutralised to 0, not dropped");

        // Guard off: the raw over-ceiling crossing reappears — the differential proves the guard is live.
        disabledEvents.OfType<SectorEvent>(DomainEventKind.Sector).Max(s => s.DeltaMs).Should().BeGreaterThan(ceiling);
        disabledEvents.OfType<SessionEvent>(DomainEventKind.Session).Single()
            .SectorAvgDeltaMs.Should().HaveCount(3);
    }

    [Fact]
    public async Task M3_complete_tier_filters_against_the_pre_overwrite_lap_deficit()
    {
        // The load-bearing case (ComputeSession :334): lap 2 seeds a genuinely SLOWER reference, lap 3 is
        // a PB that overwrites it (_reference = self at :378) yet still loses time at one corner while
        // gaining overall. The session-tier budget must reflect lap 3's PRE-overwrite deficit (a large
        // gain), not the ~0 a post-overwrite self==ref read would give — otherwise the still-valid corner
        // loss would be wrongly dropped. A refactor moving the deficit capture below MaybeUpdate fails here.
        IReadOnlyList<TelemetryFrame> baseFrames = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 4);
        IReadOnlyList<TelemetryFrame> slowRef = StretchBand(baseFrames, lapNumber: 2, from: 0f, to: 1.0f, extraMs: 6000);
        IReadOnlyList<TelemetryFrame> frames = StretchBand(slowRef, lapNumber: 3, from: 0.78f, to: 0.90f, extraMs: 1800);

        using var harness = new ComputeTestHarness();
        IReadOnlyList<DomainEvent> events = await harness.RunAsync(frames, SessionId);

        var options = new ComputeOptions();
        List<LapEvent> lapEvents = [.. events.OfType<LapEvent>(DomainEventKind.Lap)];
        LapEvent lap3 = lapEvents[1];
        lap3.IsPb.Should().BeTrue("lap 3 beats the slower reference lap 2 and overwrites the reference");
        lap3.DeltaMs.Should().BeLessThan(0, "lap 3 gained time overall — a real PB against the slow reference");

        SessionEvent session = events.OfType<SessionEvent>(DomainEventKind.Session).Single();
        session.AggregatedLosses.Should().ContainSingle(l => l.CornerId == "spa_t03");
        AggregatedLoss t03 = session.AggregatedLosses.Single(l => l.CornerId == "spa_t03");
        t03.AvgLossMs.Should().BeGreaterThan(
            options.LapDeficitFloorMs,
            "a post-overwrite self==ref deficit (~0) collapses the Tier-B budget to the floor and would drop this loss");
        Math.Abs(lap3.DeltaMs).Should().BeGreaterThan(
            t03.AvgLossMs,
            "the pre-overwrite lap-deficit budget still admits the loss — the capture is taken before MaybeUpdate");
    }

    [Fact]
    public async Task Sector_avg_delta_is_the_median_of_coachable_flying_laps_independent_of_best_sector()
    {
        // M25 end-to-end pin (SectorAvgDeltas → SectorDeltaAggregator.Median, ComputeSession :500). A slow-
        // S1 reference is seeded so every flying lap posts a NEGATIVE (gain) S1 delta; the flying laps are
        // slower overall (S3 stretch) so none overwrites that reference. The session S1 aggregate must be
        // the MEDIAN of the emitted coachable per-crossing deltas (a negative gain), the vector must stay a
        // 3-element positional per-sector list (ascending sector index), and the best-sector channel
        // (TheoreticalBestGapMs, driven by _bestSectorMs = min of absolute sector times) must stay a
        // non-negative gap — a separate computation from the signed median loss channel. Guards are made
        // permissive so this isolates the median estimator, not the M3 plausibility filter (tested above).
        const float s1End = 1f / 3f;   // sector 0 spans [0, 1/3)
        const float s3Start = 2f / 3f; // sector 2 spans [2/3, 1)

        // Seed a reference that is deliberately slow through S1 on every bounded lap, so the first clean
        // lap seeds this slow-S1 reference and no later seed lap (all identical) overwrites it.
        IReadOnlyList<TelemetryFrame> seed = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 5);
        foreach (int lap in new[] { 2, 3, 4 })
        {
            seed = StretchBand(seed, lapNumber: lap, from: 0f, to: s1End, extraMs: 6000);
        }

        using var harness = new ComputeTestHarness();
        harness.SeedReference(seed, "20260601-110000-000");

        // Flying laps: slower overall (S3 stretch keeps them behind the seeded reference so it survives),
        // with a distinct, still-negative S1 delta per lap (S1 stretched less than the reference's 6000 ms).
        IReadOnlyList<TelemetryFrame> eval = SyntheticSessionBuilder.Build(SyntheticTracks.Spa, lapCount: 5);
        foreach (int lap in new[] { 2, 3, 4 })
        {
            eval = StretchBand(eval, lapNumber: lap, from: s3Start, to: 1f, extraMs: 8000);
        }

        eval = StretchBand(eval, lapNumber: 3, from: 0f, to: s1End, extraMs: 900);
        eval = StretchBand(eval, lapNumber: 4, from: 0f, to: s1End, extraMs: 1800);

        IReadOnlyList<DomainEvent> events = await harness.RunAsync(eval, SessionId, Permissive());

        List<int> s1Deltas =
            [.. events.OfType<SectorEvent>(DomainEventKind.Sector).Where(s => s.SectorIdx == 0).Select(s => s.DeltaMs)];
        s1Deltas.Should().HaveCountGreaterThanOrEqualTo(2, "at least two coachable flying laps cross S1");
        s1Deltas.Should().OnlyContain(d => d < 0, "every flying lap gains time in S1 against the slow reference");

        SessionEvent session = events.OfType<SessionEvent>(DomainEventKind.Session).Single();
        session.SectorAvgDeltaMs.Should().HaveCount(3, "the aggregate stays a positional per-sector vector");

        int expectedMedian = SectorDeltaAggregator.Median(s1Deltas);
        session.SectorAvgDeltaMs[0].Should().Be(
            expectedMedian, "the S1 session aggregate is the median of the coachable crossing deltas");
        session.SectorAvgDeltaMs[0].Should().BeNegative("the median of the S1 gains is itself a gain, not a mean-poisoned loss");

        // Best-sector channel is independent of the signed median loss channel: it is a non-negative gap
        // built from _bestSectorMs (min of absolute clean-lap sector times), never the median delta.
        session.TheoreticalBestGapMs.Should().BeGreaterThanOrEqualTo(0);
    }

    // All-guards-off options: every plausibility ceiling/budget is effectively infinite, so the same
    // injected artefacts flow through untouched. Used only to prove (by differential) that the on-by-default
    // guard is what neutralises them — never as a production configuration.
    private static ComputeOptions Permissive() => new()
    {
        MaxPlausibleCornerLossMs = int.MaxValue,
        MaxPlausibleSectorLossMs = int.MaxValue,
        LapDeficitFloorMs = int.MaxValue,
    };

    // Rewrites frame timestamps so the [from, to) position band on `lapNumber` takes `extraMs` longer,
    // leaving every other span's internal duration unchanged (downstream frames shift later in time,
    // monotonic). Positions and every telemetry channel are untouched — duration is the only lever the
    // reference-relative delta (and thus the M3 guard) reads, so this injects a controlled per-corner /
    // per-sector loss without perturbing segmentation or the kernels.
    private static IReadOnlyList<TelemetryFrame> StretchBand(
        IReadOnlyList<TelemetryFrame> frames, int lapNumber, float from, float to, int extraMs)
    {
        List<TelemetryFrame> result = [.. frames.Select(f => f.Clone())];
        int bandCount = result.Count(f =>
            f.LapNumber == lapNumber && f.NormalizedCarPosition >= from && f.NormalizedCarPosition < to);
        if (bandCount == 0)
        {
            return result;
        }

        double perFrameMs = extraMs / (double)bandCount;
        double offsetMs = 0;
        foreach (TelemetryFrame frame in result)
        {
            if (frame.LapNumber == lapNumber
                && frame.NormalizedCarPosition >= from && frame.NormalizedCarPosition < to)
            {
                offsetMs += perFrameMs;
            }

            frame.T = Timestamp.FromDateTimeOffset(frame.T.ToDateTimeOffset() + TimeSpan.FromMilliseconds(offsetMs));
        }

        return result;
    }

    private static IReadOnlyList<TelemetryFrame> WithSingleInvalidFrame(
        IReadOnlyList<TelemetryFrame> frames, int lapNumber, float atLeastPos)
    {
        List<TelemetryFrame> result = [.. frames.Select(f => f.Clone())];
        TelemetryFrame? target = result
            .FirstOrDefault(f => f.LapNumber == lapNumber && f.NormalizedCarPosition >= atLeastPos);
        if (target is not null)
        {
            target.IsValidLap = false;
        }

        return result;
    }

    private static IReadOnlyList<TelemetryFrame> WithPitTail(
        IReadOnlyList<TelemetryFrame> frames, int lapNumber, float fromPos)
    {
        List<TelemetryFrame> result = [.. frames.Select(f => f.Clone())];
        foreach (TelemetryFrame frame in result.Where(f => f.LapNumber == lapNumber && f.NormalizedCarPosition > fromPos))
        {
            frame.IsInPitLane = true;
        }

        return result;
    }

    [Fact]
    public async Task Completes_the_domain_fan_out_even_with_no_frames()
    {
        using var harness = new ComputeTestHarness();
        DomainEventSubscription sub = harness.DomainFanOut.Subscribe("solo");

        // A session that never starts (Complete without Accept) must still close the stream.
        var session = new ComputeSession(
            harness.DomainFanOut, harness.TrackModels, harness.Centerlines, harness.Lookup, harness.OptimalLookup,
            harness.ReferenceStore, harness.Laps,
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
