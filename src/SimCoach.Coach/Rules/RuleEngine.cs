using SimCoach.Coach.Actions;

namespace SimCoach.Coach.Rules;

/// <summary>
/// Decides whether a candidate tip speaks, stays silent, or downgrades to a template — the FR-035 quiet
/// zones plus the cadence-governor (materiality floor, cross-cadence global cooldown, per-lap tip cap).
/// Pure gate logic over the valid subset, the latest-frame snapshot, session spend, and two precomputed
/// scalars (<c>timeLossMs</c> + <c>highSeverity</c>) supplied by the caller — the engine takes no
/// <c>CoachOptions</c>/severity dependency. The only state is a per-cadence cooldown map, the global
/// last-emit timestamp, and the per-lap tip counter (single-consumer = CoachService, so no locking),
/// updated via <see cref="NoteTip"/>, zeroed per lap via <see cref="ResetLap"/>, and cleared at the session
/// boundary via <see cref="ResetSession"/>. Frame-dependent gates fail OPEN when the snapshot carries no
/// live frame (<see cref="GateSnapshot.HasFrame"/> is false); a High-severity lead bypasses all three
/// cadence-governor gates (the never-silent guarantee). The global cooldown and the per-lap cap only silence
/// the cadences in <see cref="CadenceOptions.GovernedCadences"/> (Corner by default) — sector/lap summaries
/// are exempt and stay subject only to the materiality floor; the floor applies to every cadence.
/// </summary>
public sealed class RuleEngine
{
    private readonly RuleEngineOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<CoachCadence, DateTimeOffset> _lastEmit = new();
    private DateTimeOffset _lastEmitGlobal;
    private int _tipsThisLap;

    public RuleEngine(RuleEngineOptions options, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        options.EnsureValid();
        _options = options;
        _clock = clock;
    }

    public RuleDecision ShouldSpeak(
        IReadOnlyList<CoachAction> subset, CoachCadence cadence, in GateSnapshot frame, in BudgetState budget,
        double timeLossMs, bool highSeverity)
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

        // Cadence-governor (M10). A High-severity lead bypasses all three — the never-silent guarantee, the
        // same policy as M7's abstain guard, enforced here with an explicit !highSeverity conjunct so a future
        // high-priority catch-all can never be silenced by cadence governance. The global cooldown and per-lap
        // cap additionally gate only the governed cadences (Corner by default) — a sector/lap summary is exempt
        // (owner ruling: a silenced summary is more jarring than a dropped corner tip), leaving the materiality
        // floor as their sole cadence-governor gate.
        // timeLossMs == 0 means "no measured loss" (e.g. a no-PB corner with no delta_ms) — the floor fails OPEN
        // there, exactly like the frame gates, so absolute feedback is never muted for lack of a reference.
        if (!highSeverity && timeLossMs > 0 && timeLossMs < _options.Cadence.MinTimeLossMs)
        {
            return RuleDecision.Silent(QuietReason.BelowTimeLossFloor);
        }

        if (!highSeverity && IsGoverned(cadence) && InGlobalCooldown())
        {
            return RuleDecision.Silent(QuietReason.GlobalCooldown);
        }

        if (!highSeverity && IsGoverned(cadence) && _tipsThisLap >= _options.Cadence.MaxTipsPerLap)
        {
            return RuleDecision.Silent(QuietReason.LapTipBudget);
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

    /// <summary>
    /// Records that a tip was emitted for <paramref name="cadence"/>: arms its per-cadence cooldown, the global
    /// cross-cadence cooldown, and counts it against the per-lap tip budget. Silence paths (a quiet-zone or an
    /// M7 abstain) must NOT call this, so cadence budget is only spent by tips that actually speak.
    /// </summary>
    public void NoteTip(CoachCadence cadence, DateTimeOffset emittedAtUtc)
    {
        _lastEmit[cadence] = emittedAtUtc;
        _lastEmitGlobal = emittedAtUtc;
        _tipsThisLap++;
    }

    /// <summary>Zeroes the per-lap tip counter at a lap boundary so the next lap gets a fresh chattiness budget.</summary>
    public void ResetLap() => _tipsThisLap = 0;

    /// <summary>Clears all cadence state at a session boundary so a singleton engine carries no stale state.</summary>
    public void ResetSession()
    {
        _lastEmit.Clear();
        _lastEmitGlobal = default;
        _tipsThisLap = 0;
    }

    private static bool IsRealtimeGated(CoachCadence cadence) =>
        cadence is CoachCadence.Corner or CoachCadence.Sector;

    // Whether the per-lap cap and the global cooldown may silence this cadence (default: Corner only). A
    // sector/lap summary is exempt — dropping it is more jarring than dropping a corner tip.
    private bool IsGoverned(CoachCadence cadence) => _options.Cadence.GovernedCadences.Contains(cadence);

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

    private bool InGlobalCooldown()
    {
        TimeSpan cooldown = _options.Cadence.GlobalCooldown;
        return cooldown > TimeSpan.Zero && _clock.GetUtcNow() - _lastEmitGlobal < cooldown;
    }

    private bool InCooldown(CoachCadence cadence)
    {
        TimeSpan cooldown = _options.Cadence.Cooldowns[cadence];
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
