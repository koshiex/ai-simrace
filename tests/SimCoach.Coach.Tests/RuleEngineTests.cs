using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using SimCoach.Coach;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Rules;
using Xunit;

namespace SimCoach.Coach.Tests;

public sealed class RuleEngineTests
{
    private static readonly IReadOnlyList<CoachAction> _oneAction =
        [Action(CoachCadence.Corner, new CoachPriority(CoachPhase.Brake, 100))];

    [Fact]
    public void Strategy_cadence_is_always_silent_reserved()
    {
        RuleDecision decision = Engine().ShouldSpeak(_oneAction, CoachCadence.Strategy, Frame(), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.StrategyReserved));
    }

    [Fact]
    public void Empty_subset_is_silent()
    {
        RuleDecision decision = Engine().ShouldSpeak([], CoachCadence.Corner, Frame(), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.EmptySubset));
    }

    [Fact]
    public void Green_in_corner_speaks()
    {
        RuleDecision decision = Engine().ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Speak);
    }

    [Theory]
    [InlineData(SessionFlag.Pit)]
    [InlineData(SessionFlag.SafetyCar)]
    [InlineData(SessionFlag.Yellow)]
    [InlineData(SessionFlag.Paused)]
    public void Session_not_green_is_silent(SessionFlag state)
    {
        RuleDecision decision = Engine().ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(state: state), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.SessionNotGreen));
    }

    [Fact]
    public void Recent_contact_is_silent()
    {
        RuleDecision decision = Engine().ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(contact: true), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.RecentContact));
    }

    [Fact]
    public void Recent_off_track_is_silent()
    {
        RuleDecision decision = Engine().ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(offTrack: true), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.RecentOffTrack));
    }

    [Fact]
    public void User_quiet_zone_is_silent()
    {
        var options = new RuleEngineOptions { UserQuietZones = [new QuietZoneRange(0.4, 0.6)] };
        RuleDecision decision = Engine(options).ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(pos: 0.5), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.UserZone));
    }

    [Fact]
    public void Apex_window_is_silent_for_realtime()
    {
        RuleDecision decision =
            Engine().ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(phase: GateCornerPhase.Apex), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.ApexWindow));
    }

    [Fact]
    public void On_a_straight_is_silent_for_realtime()
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(phase: GateCornerPhase.None, steer: 0.0, speed: 220), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.Straight));
    }

    [Fact]
    public void High_workload_is_silent_for_realtime()
    {
        RuleDecision decision = Engine().ShouldSpeak(
            _oneAction, CoachCadence.Corner, Frame(brake: 1.0, steer: 0.9), BudgetState.Zero);
        decision.Should().Be(RuleDecision.Silent(QuietReason.Workload));
    }

    [Fact]
    public void Lap_cadence_bypasses_apex_and_workload_gates()
    {
        // Apex + high workload would silence a corner tip; a lap tip ignores the real-time-only gates.
        GateSnapshot hostile = Frame(phase: GateCornerPhase.Apex, steer: 0.9, brake: 1.0);
        IReadOnlyList<CoachAction> lapAction = [Action(CoachCadence.Lap, new CoachPriority(CoachPhase.Brake, 100))];

        RuleDecision decision = Engine().ShouldSpeak(lapAction, CoachCadence.Lap, hostile, BudgetState.Zero);

        decision.Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Cooldown_silences_within_window_and_clears_after()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var engine = new RuleEngine(new RuleEngineOptions(), clock);

        engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow());

        clock.Advance(TimeSpan.FromSeconds(2));
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero)
            .Should().Be(RuleDecision.Silent(QuietReason.Cooldown));

        clock.Advance(TimeSpan.FromSeconds(3)); // total 5 s > 4 s corner cooldown
        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero)
            .Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Reset_session_clears_cooldown()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        var engine = new RuleEngine(new RuleEngineOptions(), clock);
        engine.NoteTip(CoachCadence.Corner, clock.GetUtcNow());

        engine.ResetSession();

        engine.ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), BudgetState.Zero).Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Priority_floor_silences_when_best_action_is_too_weak()
    {
        var options = new RuleEngineOptions { PriorityFloor = new CoachPriority(CoachPhase.Brake, 50) };
        IReadOnlyList<CoachAction> weak = [Action(CoachCadence.Corner, new CoachPriority(CoachPhase.Exit, 10))];

        RuleDecision decision = Engine(options).ShouldSpeak(weak, CoachCadence.Corner, Frame(), BudgetState.Zero);

        decision.Should().Be(RuleDecision.Silent(QuietReason.PriorityFloor));
    }

    [Fact]
    public void Over_session_budget_downgrades_to_template_only()
    {
        var options = new RuleEngineOptions { SessionBudgetUsd = 0.50m };
        RuleDecision decision = Engine(options)
            .ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), new BudgetState(0.50m, 0m));
        decision.Should().Be(RuleDecision.TemplateOnly(QuietReason.OverBudget));
    }

    [Fact]
    public void Over_monthly_budget_downgrades_to_template_only()
    {
        var options = new RuleEngineOptions { MonthlyBudgetUsd = 5.00m };
        RuleDecision decision = Engine(options)
            .ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), new BudgetState(0m, 5.00m));
        decision.Should().Be(RuleDecision.TemplateOnly(QuietReason.OverBudget));
    }

    [Fact]
    public void Zero_monthly_budget_means_no_monthly_cap()
    {
        // Default MonthlyBudgetUsd = 0 → a large rolling spend must not trip the monthly gate.
        RuleDecision decision = Engine()
            .ShouldSpeak(_oneAction, CoachCadence.Corner, Frame(), new BudgetState(0m, 999m));
        decision.Should().Be(RuleDecision.Speak);
    }

    [Fact]
    public void Unknown_snapshot_fails_open_skipping_frame_gates()
    {
        // A user zone covering position 0 must NOT fire on the Unknown sentinel (position 0, HasFrame=false);
        // default(GateSnapshot) would misfire here.
        var options = new RuleEngineOptions { UserQuietZones = [new QuietZoneRange(0.0, 0.1)] };

        RuleDecision decision =
            Engine(options).ShouldSpeak(_oneAction, CoachCadence.Corner, GateSnapshot.Unknown, BudgetState.Zero);

        decision.Should().Be(RuleDecision.Speak);
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
