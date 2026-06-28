using SimCoach.Coach.Actions;

namespace SimCoach.Coach.Rules;

/// <summary>
/// Decides whether a candidate tip speaks, stays silent, or downgrades to a template — the FR-035 quiet
/// zones. Pure gate logic over the valid subset, the latest-frame snapshot, and session spend; the only
/// state is a per-cadence cooldown map (single-consumer = CoachService, so no locking) updated via
/// <see cref="NoteTip"/> and cleared at the session boundary via <see cref="ResetSession"/>. Frame-dependent
/// gates fail OPEN when the snapshot carries no live frame (<see cref="GateSnapshot.HasFrame"/> is false).
/// </summary>
public sealed class RuleEngine
{
    private readonly RuleEngineOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<CoachCadence, DateTimeOffset> _lastEmit = new();

    public RuleEngine(RuleEngineOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.EnsureValid();
        _options = options;
        _clock = clock;
    }

    public RuleDecision ShouldSpeak(
        IReadOnlyList<CoachAction> subset, CoachCadence cadence, in GateSnapshot frame, in BudgetState budget)
    {
        ArgumentNullException.ThrowIfNull(subset);

        // Strategy is reserved: the seam exists but no Strategy tip is emitted in the MVP.
        if (cadence == CoachCadence.Strategy)
        {
            return RuleDecision.Silent(QuietReason.StrategyReserved);
        }

        if (subset.Count == 0)
        {
            return RuleDecision.Silent(QuietReason.EmptySubset);
        }

        // Frame-dependent gates — fail open without a live frame.
        if (frame.HasFrame)
        {
            if (frame.SessionState != SessionFlag.Green)
            {
                return RuleDecision.Silent(QuietReason.SessionNotGreen);
            }

            if (frame.Contact)
            {
                return RuleDecision.Silent(QuietReason.RecentContact);
            }

            if (frame.OffTrack)
            {
                return RuleDecision.Silent(QuietReason.RecentOffTrack);
            }

            if (InUserQuietZone(frame.NormalizedCarPosition))
            {
                return RuleDecision.Silent(QuietReason.UserZone);
            }

            // Lap/Session bypass the straight/apex/workload gates (the car may be on a straight or in the pits
            // when those tips land); only Corner/Sector honor them.
            if (IsRealtimeGated(cadence))
            {
                if (frame.CornerPhase == GateCornerPhase.Apex)
                {
                    return RuleDecision.Silent(QuietReason.ApexWindow);
                }

                if (IsOnStraight(frame))
                {
                    return RuleDecision.Silent(QuietReason.Straight);
                }

                if (IsHighWorkload(frame))
                {
                    return RuleDecision.Silent(QuietReason.Workload);
                }
            }
        }

        if (InCooldown(cadence))
        {
            return RuleDecision.Silent(QuietReason.Cooldown);
        }

        if (BestPriority(subset) > _options.PriorityFloor)
        {
            return RuleDecision.Silent(QuietReason.PriorityFloor);
        }

        return IsOverBudget(budget)
            ? RuleDecision.TemplateOnly(QuietReason.OverBudget)
            : RuleDecision.Speak;
    }

    private bool IsOverBudget(in BudgetState budget) =>
        budget.SessionCostUsd >= _options.SessionBudgetUsd ||
        (_options.MonthlyBudgetUsd > 0 && budget.RollingMonthlyCostUsd >= _options.MonthlyBudgetUsd);

    /// <summary>Records that a tip was emitted for <paramref name="cadence"/>, arming its cooldown.</summary>
    public void NoteTip(CoachCadence cadence, DateTimeOffset emittedAtUtc) => _lastEmit[cadence] = emittedAtUtc;

    /// <summary>Clears all cooldowns at a session boundary so a singleton engine carries no stale state.</summary>
    public void ResetSession() => _lastEmit.Clear();

    private static bool IsRealtimeGated(CoachCadence cadence) =>
        cadence is CoachCadence.Corner or CoachCadence.Sector;

    private bool IsOnStraight(in GateSnapshot frame) =>
        Math.Abs(frame.Steer) < _options.StraightMaxSteer && frame.SpeedKmh > _options.StraightMinSpeedKmh;

    private bool IsHighWorkload(in GateSnapshot frame) =>
        frame.Brake + Math.Abs(frame.Steer) > _options.WorkloadBrakeSteerSum ||
        Math.Abs(frame.SteerRate) > _options.WorkloadSteerRate;

    private bool InUserQuietZone(double position)
    {
        foreach (QuietZoneRange zone in _options.UserQuietZones)
        {
            if (zone.Contains(position))
            {
                return true;
            }
        }

        return false;
    }

    private bool InCooldown(CoachCadence cadence)
    {
        TimeSpan cooldown = _options.Cooldowns[cadence];
        if (cooldown <= TimeSpan.Zero || !_lastEmit.TryGetValue(cadence, out DateTimeOffset last))
        {
            return false;
        }

        return _clock.GetUtcNow() - last < cooldown;
    }

    private static CoachPriority BestPriority(IReadOnlyList<CoachAction> subset)
    {
        CoachPriority best = subset[0].Priority;
        for (int i = 1; i < subset.Count; i++)
        {
            if (subset[i].Priority < best)
            {
                best = subset[i].Priority;
            }
        }

        return best;
    }
}
