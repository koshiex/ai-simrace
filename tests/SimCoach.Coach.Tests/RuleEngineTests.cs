using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using SimCoach.Coach;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Rules;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class RuleEngineTests
{
    // A loss comfortably above the default MinTimeLossMs floor, so the materiality gate is not the one under test.
    private const double MaterialLossMs = 1000.0;

    private static readonly IReadOnlyList<CoachAction> _oneAction =
        [Action(CoachCadence.Corner, new CoachPriority(CoachPhase.Brake, 100))];

    [Fact]
    public void Strategy_cadence_is_always_silent_reserved()
    {
        RuleDecision decision =
            Engine().ShouldSpeak(_oneAction, CoachCadence.Strategy, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.StrategyReserved));
    }

    [Fact]
    public void Empty_subset_is_silent()
    {
        RuleDecision decision =
            Engine().ShouldSpeak([], CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.EmptySubset));
    }

    [Fact]
    public void Green_in_corner_speaks()
    {
        RuleDecision decision =
            Engine().ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Speak);
    }

    [Theory]
    [InlineData(SessionFlag.Pit)]
    [InlineData(SessionFlag.SafetyCar)]
    [InlineData(SessionFlag.Yellow)]
    [InlineData(SessionFlag.Paused)]
    public void Session_not_green_is_silent(SessionFlag state)
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(state: state), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.SessionNotGreen));
    }

    [Fact]
    public void Recent_contact_is_silent()
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(contact: true), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.RecentContact));
    }

    [Fact]
    public void Recent_off_track_is_silent()
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(offTrack: true), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.RecentOffTrack));
    }

    [Fact]
    public void User_quiet_zone_is_silent()
    {
        var options = new RuleEngineOptions { UserQuietZones = [new QuietZoneRange(0.4, 0.6)] };
        RuleDecision decision = Engine(options).ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(pos: 0.5), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.UserZone));
    }

    [Fact]
    public void Apex_window_is_silent_for_realtime()
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(phase: GateCornerPhase.Apex), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.ApexWindow));
    }

    [Fact]
    public void On_a_straight_is_silent_for_realtime()
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(phase: GateCornerPhase.None, steer: 0.0, speed: 220),
            BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.Straight));
    }

    [Fact]
    public void High_workload_is_silent_for_realtime()
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(brake: 1.0, steer: 0.9), BudgetState.Zero, MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.Workload));
    }

    [Fact]
    public void Lap_cadence_bypasses_apex_and_workload_gates()
    {
        // Apex + high workload would silence a corner tip; a lap tip ignores the real-time-only gates.
        GateSnapshot hostile = Frame(phase: GateCornerPhase.Apex, steer: 0.9, brake: 1.0);
        IReadOnlyList<CoachAction> lapAction = [Action(CoachCadence.Lap, new CoachPriority(CoachPhase.Brake, 100))];

        RuleDecision decision =
            Engine().ShouldSpeak(lapAction, CoachCadence.Lap, hostile, BudgetState.Zero, MaterialLossMs, highSeverity: false);

        decision.Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Cooldown_silences_within_window_and_clears_after()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        // Isolate the per-cadence cooldown from the global cooldown, which would otherwise fire first.
        var options = new RuleEngineOptions { Cadence = new CadenceOptions { GlobalCooldown = TimeSpan.Zero } };
        var engine = new RuleEngine(options, clock);

        engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow());

        clock.Advance(TimeSpan.FromSeconds(2));
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Silent(QuietReason.Cooldown));

        clock.Advance(TimeSpan.FromSeconds(3)); // total 5 s > 4 s corner cooldown
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Reset_session_clears_cooldown_lap_counter_and_global()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var engine = new RuleEngine(new RuleEngineOptions(), clock);
        for (int i = 0; i < 5; i++) // fill the per-lap budget and arm both cooldowns
        {
            engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow());
        }

        engine.ResetSession();

        // Same instant: per-cadence cooldown, global cooldown, and the per-lap cap would all still bite if any
        // of that state survived the reset — so a plain Speak proves every counter/timestamp was cleared.
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Priority_floor_silences_when_best_action_is_too_weak()
    {
        var options = new RuleEngineOptions { PriorityFloor = new CoachPriority(CoachPhase.Brake, 50) };
        IReadOnlyList<CoachAction> weak = [Action(CoachCadence.Corner, new CoachPriority(CoachPhase.Exit, 10))];

        RuleDecision decision = Engine(options).ShouldSpeak(
            weak, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false);

        decision.Should().Be(RuleDecision.Silent(QuietReason.PriorityFloor));
    }

    [Fact]
    public void Over_session_budget_downgrades_to_template_only()
    {
        var options = new RuleEngineOptions { SessionBudgetUsd = 0.50m };
        RuleDecision decision = Engine(options).ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(), new BudgetState(0.50m, 0m), MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.TemplateOnly(QuietReason.OverBudget));
    }

    [Fact]
    public void Over_monthly_budget_downgrades_to_template_only()
    {
        var options = new RuleEngineOptions { MonthlyBudgetUsd = 5.00m };
        RuleDecision decision = Engine(options).ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(), new BudgetState(0m, 5.00m), MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.TemplateOnly(QuietReason.OverBudget));
    }

    [Fact]
    public void Zero_monthly_budget_means_no_monthly_cap()
    {
        // Default MonthlyBudgetUsd = 0 → a large rolling spend must not trip the monthly gate.
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(), new BudgetState(0m, 999m), MaterialLossMs, highSeverity: false);
        decision.Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Unknown_snapshot_fails_open_skipping_frame_gates()
    {
        // A user zone covering position 0 must NOT fire on the Unknown sentinel (position 0, HasFrame=false);
        // default(GateSnapshot) would misfire here.
        var options = new RuleEngineOptions { UserQuietZones = [new QuietZoneRange(0.0, 0.1)] };

        RuleDecision decision = Engine(options).ShouldSpeak(
            _oneAction, CoachCadence.Corner, GateSnapshot.Unknown, BudgetState.Zero, MaterialLossMs, highSeverity: false);

        decision.Should().Be(RuleDecision.Speak);
    }

    // ---- M10 cadence-governor ------------------------------------------------------------------------

    [Fact]
    public void Below_the_materiality_floor_is_silent()
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, timeLossMs: 50.0, highSeverity: false);
        decision.Should().Be(RuleDecision.Silent(QuietReason.BelowTimeLossFloor));
    }

    [Fact]
    public void At_the_materiality_floor_speaks()
    {
        // Default MinTimeLossMs = 100; the floor is strict (< floor is silent), so exactly-at speaks.
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, timeLossMs: 100.0, highSeverity: false);
        decision.Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void High_severity_bypasses_the_materiality_floor()
    {
        // The same below-floor loss that silences an ordinary tip must speak when High — this pins the floor's
        // !highSeverity conjunct (deleting it would silence this High tip and fail the test).
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, timeLossMs: 50.0, highSeverity: true);
        decision.Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Zero_time_loss_fails_the_floor_open()
    {
        // 0 = "no measured loss" (e.g. a no-PB corner with no delta_ms), the fail-open sentinel — absolute
        // feedback is never muted for lack of a reference, so an ordinary tip still speaks.
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, timeLossMs: 0.0, highSeverity: false);
        decision.Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Global_cooldown_silences_across_cadences_and_clears_after()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var engine = new RuleEngine(new RuleEngineOptions(), clock); // GlobalCooldown = 3 s

        // A *sector* tip arms the global cooldown even though sector itself is exempt from being silenced by it.
        engine.NoteTip(CoachCadence.Sector, clock.GetUtcNow());

        // A governed corner tip within the global window is muted even though its own per-cadence cooldown is unarmed.
        clock.Advance(TimeSpan.FromSeconds(1));
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Silent(QuietReason.GlobalCooldown));

        clock.Advance(TimeSpan.FromSeconds(2.5)); // total 3.5 s > 3 s global
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Per_lap_cap_silences_once_the_budget_is_spent_and_reset_lap_reopens()
    {
        // Disable the global cooldown so the per-lap cap is the gate under test (not the timestamp); advance the
        // clock past the 4 s corner per-cadence cooldown so it is not the confound either.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new RuleEngineOptions { Cadence = new CadenceOptions { GlobalCooldown = TimeSpan.Zero } };
        var engine = new RuleEngine(options, clock);
        for (int i = 0; i < options.Cadence.MaxTipsPerLap; i++) // spend the whole per-lap budget on corner tips
        {
            engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow());
        }

        clock.Advance(TimeSpan.FromSeconds(5)); // clear the 4 s corner per-cadence cooldown
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Silent(QuietReason.LapTipBudget));

        engine.ResetLap();

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Sector_and_lap_summaries_bypass_the_cap_and_global_cooldown_while_a_corner_is_silenced()
    {
        // Owner ruling: the per-lap cap and the global cooldown govern Corner only. On a fresh engine with a
        // spent per-lap budget AND an active global cooldown, a sector and a lap summary each still speak, while
        // a corner tip in the same state is silenced.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var engine = new RuleEngine(new RuleEngineOptions(), clock);
        for (int i = 0; i < 5; i++) // spend the per-lap budget (MaxTipsPerLap = 5) and arm the 3 s global cooldown
        {
            engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow());
        }

        clock.Advance(TimeSpan.FromSeconds(1)); // still inside the global window, budget spent
        IReadOnlyList<CoachAction> sector = [Action(CoachCadence.Sector, new CoachPriority(CoachPhase.Brake, 100))];
        IReadOnlyList<CoachAction> lap = [Action(CoachCadence.Lap, new CoachPriority(CoachPhase.Brake, 100))];

        engine.ShouldSpeak(sector, CoachCadence.Sector, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Speak, "a sector summary is exempt from the cap and the global cooldown");
        engine.ShouldSpeak(lap, CoachCadence.Lap, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Speak, "a lap summary is exempt from the cap and the global cooldown");

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Outcome.Should().Be(RuleOutcome.Silent, "a corner tip in the same state is governed and silenced");
    }

    [Fact]
    public void High_severity_bypasses_all_three_silence_sources_at_once()
    {
        // Never-silent guarantee: a High-severity lead must speak even when the materiality floor, the global
        // cooldown, AND the per-lap cap would each independently silence an ordinary tip.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var engine = new RuleEngine(new RuleEngineOptions(), clock);
        for (int i = 0; i < 5; i++) // fill the per-lap budget (MaxTipsPerLap = 5) and arm the global cooldown via
        {                            // sector tips, leaving the corner per-cadence cooldown unarmed as the vehicle
            engine.NoteTip(CoachCadence.Sector, clock.GetUtcNow());
        }

        // A governed corner tip: inside the 3 s global window, over the per-lap cap, and below the 100 ms floor
        // (50 ms > 0, so the floor genuinely bites) — each would independently silence it.
        GateSnapshot frame = Frame();
        clock.Advance(TimeSpan.FromSeconds(1));

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, frame, BudgetState.Zero, timeLossMs: 50.0, highSeverity: false)
            .Outcome.Should().Be(RuleOutcome.Silent, "an ordinary tip is silenced by the cadence governor here");

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, frame, BudgetState.Zero, timeLossMs: 50.0, highSeverity: true)
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void High_severity_bypasses_the_per_cadence_cooldown_too()
    {
        // Never-silent guarantee, fourth lever: a High-severity lead must speak even inside the same-cadence
        // cooldown window that silences an ordinary tip — this pins the !highSeverity conjunct on the per-cadence
        // cooldown branch (deleting it would silence this High tip and fail the test). Isolate the per-cadence
        // cooldown from the global cooldown (which High would also bypass) so the cooldown under test is the gate.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new RuleEngineOptions { Cadence = new CadenceOptions { GlobalCooldown = TimeSpan.Zero } };
        var engine = new RuleEngine(options, clock);

        engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow()); // arm the 4 s corner per-cadence cooldown
        clock.Advance(TimeSpan.FromSeconds(2)); // well inside the 4 s window

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: false)
            .Should().Be(RuleDecision.Silent(QuietReason.Cooldown), "an ordinary tip is silenced by the per-cadence cooldown here");

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs, highSeverity: true)
            .Should().Be(RuleDecision.Speak, "a High-severity lead bypasses the per-cadence cooldown too");
    }

    // ---- M32 cross-lap dedup -------------------------------------------------------------------------

    private static TipIdentity Identity(string corner, string action) => new(corner, action);

    // A dedup engine with every M10 timestamp lever disabled (all per-cadence cooldowns and the global cooldown
    // zeroed), so the ONLY thing that can silence a tip in these tests is the M32 dedup gate under test — never a
    // stray cooldown armed by the prior NoteTip.
    private static RuleEngine DedupEngine(int horizon = 2, int highHorizon = 3) =>
        new(
            new RuleEngineOptions
            {
                Cadence = new CadenceOptions
                {
                    RepeatSuppressionLaps = horizon,
                    HighSeverityRepeatSuppressionLaps = highHorizon,
                    GlobalCooldown = TimeSpan.Zero,
                    Cooldowns = ZeroCooldowns(),
                },
            },
            TimeProvider.System);

    private static IReadOnlyDictionary<CoachCadence, TimeSpan> ZeroCooldowns() =>
        Enum.GetValues<CoachCadence>().ToDictionary(c => c, _ => TimeSpan.Zero);

    [Fact]
    public void Repeat_action_for_same_corner_next_visit_is_suppressed_for_both_severities()
    {
        // M32-high-dedup: a High-severity repeat is deduped too (no bypass) — both severities are suppressed one
        // lap after the same (corner, action) was spoken, since 1 lap is inside both the non-High (2) and the
        // High (3) horizons.
        RuleEngine engine = DedupEngine();
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");
        engine.ResetLap(); // one lap elapsed → inside both horizons

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Silent(QuietReason.RepeatSuppressed));

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: true, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Silent(QuietReason.RepeatSuppressed),
                "a High-severity repeat is deduped over its own longer horizon, not bypassed");
    }

    [Fact]
    public void High_severity_repeat_uses_the_longer_horizon_after_the_non_high_horizon_elapses()
    {
        // RepeatSuppressionLaps = 2, HighSeverityRepeatSuppressionLaps = 3. Two laps after the tip, the non-High
        // horizon has elapsed (lapsSince 2 is not < 2) so a non-High repeat speaks, but the longer High horizon
        // (2 < 3) still suppresses the same repeat when it is High-severity.
        RuleEngine engine = DedupEngine(horizon: 2, highHorizon: 3);
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");
        engine.ResetLap();
        engine.ResetLap(); // two laps since

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Speak, "the non-High horizon has elapsed");

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: true, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Silent(QuietReason.RepeatSuppressed),
                "the longer High horizon still suppresses the same repeat");
    }

    [Fact]
    public void High_severity_repeat_resurfaces_once_the_longer_horizon_elapses()
    {
        RuleEngine engine = DedupEngine(horizon: 2, highHorizon: 3);
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");
        engine.ResetLap();
        engine.ResetLap();
        engine.ResetLap(); // three laps since → High horizon (3) elapsed

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: true, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Speak, "a genuinely costly recurring corner resurfaces after the longer horizon");
    }

    [Fact]
    public void High_severity_within_lap_idempotency_holds_even_with_the_high_horizon_off()
    {
        // HighSeverityRepeatSuppressionLaps = 0 turns off the cross-lap High horizon, but the always-on within-lap
        // idempotency clause still stops the same High (corner, action) speaking twice in one lap.
        RuleEngine engine = DedupEngine(horizon: 2, highHorizon: 0);
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: true, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Silent(QuietReason.RepeatSuppressed));

        // Next lap with the High horizon off lets the same High action speak again.
        engine.ResetLap();
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: true, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Different_action_or_different_corner_still_speaks()
    {
        RuleEngine engine = DedupEngine();
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");
        engine.ResetLap();

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionY"))
            .Should().Be(RuleDecision.Speak, "a different action for the same corner is not a repeat");
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerB", "actionX"))
            .Should().Be(RuleDecision.Speak, "the same action for a different corner is not a repeat");
    }

    [Fact]
    public void Repeat_speaks_again_after_the_horizon_elapses()
    {
        RuleEngine engine = DedupEngine(); // RepeatSuppressionLaps = 2
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");

        engine.ResetLap(); // 1 lap since → suppressed
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Silent(QuietReason.RepeatSuppressed));

        engine.ResetLap(); // 2 laps since → horizon elapsed, speaks again
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Within_lap_idempotency_suppresses_even_with_the_horizon_off()
    {
        // RepeatSuppressionLaps = 0 disables the cross-lap horizon, but the same (corner, action) still never
        // speaks twice in one lap (no ResetLap between the emit and the re-check).
        var options = new RuleEngineOptions
        {
            Cadence = new CadenceOptions
            {
                RepeatSuppressionLaps = 0,
                GlobalCooldown = TimeSpan.Zero,
                Cooldowns = ZeroCooldowns(),
            },
        };
        var engine = new RuleEngine(options, TimeProvider.System);
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Silent(QuietReason.RepeatSuppressed));

        // A new lap with the horizon off lets the same action speak again.
        engine.ResetLap();
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void No_corner_id_fails_the_dedup_gate_open()
    {
        RuleEngine engine = DedupEngine();
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");

        // A blank corner_id (a sector/lap summary) can never match the memory — it always speaks.
        engine.ShouldSpeak(_oneAction, CoachCadence.Sector, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity(string.Empty, "actionX"))
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Reset_session_clears_the_dedup_memory_but_reset_lap_does_not()
    {
        RuleEngine engine = DedupEngine();
        engine.NoteTip(CoachCadence.Corner, DateTimeOffset.UtcNow, "cornerA", "actionX");
        engine.ResetLap(); // memory survives a lap boundary → still suppressed

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Silent(QuietReason.RepeatSuppressed));

        engine.ResetSession(); // a session boundary clears it → speaks again on the same identity
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void A_repeat_suppression_does_not_disturb_the_m10_per_lap_counter()
    {
        // A suppressed repeat must arm no cooldown and spend no per-lap budget. Disable the timestamp levers so
        // the per-lap cap is the only remaining M10 gate, then prove the dedup silence did not consume it.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var options = new RuleEngineOptions { Cadence = new CadenceOptions { GlobalCooldown = TimeSpan.Zero } };
        var engine = new RuleEngine(options, clock);
        engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow(), "cornerA", "actionX"); // one real tip: 1/5 spent
        engine.ResetLap();

        // The repeat is suppressed…
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "actionX"))
            .Should().Be(RuleDecision.Silent(QuietReason.RepeatSuppressed));

        // …and after ResetLap the counter is 0 again, so four more distinct tips plus the cap boundary behave
        // exactly as M10 dictates — the suppressed repeat consumed none of the budget.
        clock.Advance(TimeSpan.FromSeconds(5)); // clear the corner per-cadence cooldown
        for (int i = 0; i < options.Cadence.MaxTipsPerLap; i++)
        {
            engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                    highSeverity: false, Identity("cornerA", $"fresh{i}"))
                .Outcome.Should().Be(RuleOutcome.Speak, "a fresh action is not a repeat");
            engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow(), "cornerA", $"fresh{i}");
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero, MaterialLossMs,
                highSeverity: false, Identity("cornerA", "another"))
            .Should().Be(RuleDecision.Silent(QuietReason.LapTipBudget), "the cap is spent by the five real tips");
    }

    [Fact]
    public void Cadence_options_reject_a_negative_repeat_suppression_horizon()
    {
        new CadenceOptions { RepeatSuppressionLaps = -1 }
            .Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cadence_options_reject_a_negative_high_severity_repeat_suppression_horizon()
    {
        new CadenceOptions { HighSeverityRepeatSuppressionLaps = -1 }
            .Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cadence_options_reject_a_negative_time_loss_floor()
    {
        new CadenceOptions { MinTimeLossMs = -1.0 }
            .Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cadence_options_reject_a_negative_global_cooldown()
    {
        new CadenceOptions { GlobalCooldown = TimeSpan.FromSeconds(-1) }
            .Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Cadence_options_reject_a_non_positive_per_lap_cap()
    {
        new CadenceOptions { MaxTipsPerLap = 0 }
            .Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    private static RuleEngine Engine(RuleEngineOptions? options = null) =>
        new(options ?? new RuleEngineOptions(), TimeProvider.System);

    private static CoachAction Action(CoachCadence cadence, CoachPriority priority) =>
        new("act", "act", cadence, priority, RequiresReference: false, [], [], "Фраза.", "hint");

    private static GateSnapshot Frame(
        GateCornerPhase phase = GateCornerPhase.Entry,
        SessionFlag state = SessionFlag.Green,
        double brake = 0.0,
        double steer = 0.2,
        double steerRate = 0.0,
        double speed = 80.0,
        bool offTrack = false,
        bool contact = false,
        double pos = 0.5) =>
        new(brake, steer, steerRate, speed, offTrack, contact, pos, phase, state, HasFrame: true);
}
