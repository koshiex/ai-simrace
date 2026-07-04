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
/// live frame (<see cref="GateSnapshot.HasFrame"/> is false); a High-severity lead bypasses ALL FOUR cadence
/// silence sources — the materiality floor, the per-cadence cooldown, the global cooldown, and the per-lap cap
/// (the never-silent guarantee), each enforced with an explicit <c>!highSeverity</c> conjunct. The global
/// cooldown and the per-lap cap only silence
/// the cadences in <see cref="CadenceOptions.GovernedCadences"/> (Corner by default) — sector/lap summaries
/// are exempt and stay subject only to the materiality floor; the floor applies to every cadence.
/// M32 layers a cross-lap dedup gate on top: the same advice for the same corner is silenced
/// (<see cref="QuietReason.RepeatSuppressed"/>) within a lap horizon, keyed by <c>corner_id</c> over a
/// monotonic lap ordinal (bumped in <see cref="ResetLap"/>) and cleared only in <see cref="ResetSession"/>.
/// Unlike the cadence-governor, this gate applies to High-severity tips too (M32-high-dedup): a non-High repeat
/// uses <see cref="CadenceOptions.RepeatSuppressionLaps"/> while a High-severity repeat uses the longer
/// <see cref="CadenceOptions.HighSeverityRepeatSuppressionLaps"/> so a genuinely costly recurring corner still
/// resurfaces periodically; the within-lap idempotency clause holds for every severity. That memory is orthogonal
/// to the cadence-governor state and, like it, is single-consumer (no locking).
/// </summary>
public sealed class RuleEngine
{
    private readonly RuleEngineOptions _options;
    private readonly TimeProvider _clock;
    private readonly Dictionary<CoachCadence, DateTimeOffset> _lastEmit = new();
    private readonly Dictionary<string, LastCornerTip> _lastCornerTip = new(StringComparer.Ordinal);
    private DateTimeOffset _lastEmitGlobal;
    private int _tipsThisLap;
    private int _lapOrdinal;

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
        double timeLossMs, bool highSeverity, in TipIdentity identity = default)
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

        // Cross-lap dedup (M32) — a semantic silence orthogonal to the M10 cadence-governor below: suppress the
        // SAME advice (exact action_id) for the SAME corner within the recent-lap horizon, plus an always-on
        // within-lap idempotency clause, so a stateless CornerEvent stops re-saying a word-for-word repeat lap
        // after lap. M32-high-dedup: unlike the M10 governor, a High-severity lead does NOT bypass this — it is
        // deduped too, just over the longer HighSeverityRepeatSuppressionLaps horizon, so a genuinely costly
        // recurring corner still resurfaces periodically instead of repeating every lap. A blank corner_id fails
        // it open. Checked before the cadence-governor so a repeat reports RepeatSuppressed regardless of cooldown
        // timing; a suppressed repeat never reaches NoteTip, so it arms no cooldown and disturbs no M10 counter.
        if (IsRepeatSuppressed(identity, highSeverity))
        {
            return RuleDecision.Silent(QuietReason.RepeatSuppressed);
        }

        // Cadence-governor (M10) + the per-cadence cooldown below. A High-severity lead bypasses ALL FOUR
        // silence sources — the materiality floor, the per-cadence cooldown, the global cooldown, and the
        // per-lap cap — the never-silent guarantee, the same policy as M7's abstain guard, enforced here with an
        // explicit !highSeverity conjunct on each so a future high-priority catch-all can never be silenced by
        // cadence governance. The global cooldown and per-lap cap additionally gate only the governed cadences
        // (Corner by default) — a sector/lap summary is exempt (owner ruling: a silenced summary is more jarring
        // than a dropped corner tip), leaving the materiality floor as their sole cadence-governor gate.
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

        // Per-cadence cooldown (Corner 4 s / Sector 8 s). A High-severity lead bypasses it too — the fourth and
        // last never-silent lever — so a High tip is never silenced inside the same-cadence cooldown window.
        if (!highSeverity && InCooldown(cadence))
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
    /// M7 abstain) must NOT call this, so cadence budget is only spent by tips that actually speak. M32: when a
    /// <paramref name="cornerId"/>/<paramref name="actionId"/> pair is supplied (a real-time corner tip), it is
    /// recorded as the corner's last spoken action at the current lap ordinal so the next visit can dedup a
    /// repeat. This records the ACTUAL spoken action (<c>tip.ActionId</c>), which may differ from the lead the
    /// pre-LLM gate read — keeping lead-vs-spoken from desyncing. A blank pair (sector/lap summaries) records
    /// no corner memory.
    /// </summary>
    public void NoteTip(CoachCadence cadence, DateTimeOffset emittedAtUtc, string? cornerId = null, string? actionId = null)
    {
        _lastEmit[cadence] = emittedAtUtc;
        _lastEmitGlobal = emittedAtUtc;
        _tipsThisLap++;
        if (!string.IsNullOrEmpty(cornerId) && !string.IsNullOrEmpty(actionId))
        {
            _lastCornerTip[cornerId] = new LastCornerTip(actionId, _lapOrdinal);
        }
    }

    /// <summary>
    /// Zeroes the per-lap tip counter at a lap boundary so the next lap gets a fresh chattiness budget, and
    /// bumps the monotonic lap ordinal that the M32 cross-lap dedup measures its horizon over. The per-corner
    /// memory itself is deliberately NOT cleared here — that is what lets dedup span laps.
    /// </summary>
    public void ResetLap()
    {
        _tipsThisLap = 0;
        _lapOrdinal++;
    }

    /// <summary>Clears all cadence state at a session boundary so a singleton engine carries no stale state.</summary>
    public void ResetSession()
    {
        _lastEmit.Clear();
        _lastEmitGlobal = default;
        _tipsThisLap = 0;
        _lastCornerTip.Clear();
        _lapOrdinal = 0;
    }

    /// <summary>
    /// M32 cross-lap dedup predicate. Fails OPEN (never suppresses) with no corner identity — sector/lap
    /// summaries carry no corner_id, matching the frame-gate discipline. Suppresses only when the corner's last
    /// recorded action equals <paramref name="identity"/>'s action AND it is either the same lap (always-on
    /// within-lap idempotency, independent of the horizon knob and of severity) or within the applicable horizon
    /// of it. M32-high-dedup: a High-severity repeat uses the longer HighSeverityRepeatSuppressionLaps horizon, a
    /// non-High repeat the ordinary RepeatSuppressionLaps. Keys on the exact action_id for now; aligns to M21's
    /// action-family key as a fast-follow.
    /// Public so <c>CoachService</c> can re-check the ACTUAL chosen action post-LLM: the pre-LLM gate inside
    /// <see cref="ShouldSpeak"/> keys on the lead (subset[0]) to save the call, but the model may deterministically
    /// pick a non-lead subset member — that repeat must still dedup instead of re-speaking every lap. This is a
    /// pure read (no state change); <see cref="NoteTip"/> remains the only writer.
    /// </summary>
    public bool IsRepeatSuppressed(in TipIdentity identity, bool highSeverity)
    {
        if (string.IsNullOrEmpty(identity.CornerId) || string.IsNullOrEmpty(identity.ActionId))
        {
            return false;
        }

        if (!_lastCornerTip.TryGetValue(identity.CornerId, out LastCornerTip last) ||
            !string.Equals(last.ActionId, identity.ActionId, StringComparison.Ordinal))
        {
            return false;
        }

        int lapsSince = _lapOrdinal - last.LapOrdinal;
        if (lapsSince == 0)
        {
            return true; // within-lap idempotency: never say the same thing twice in one lap, even with the horizon off
        }

        int horizon = highSeverity
            ? _options.Cadence.HighSeverityRepeatSuppressionLaps
            : _options.Cadence.RepeatSuppressionLaps;
        return horizon > 0 && lapsSince < horizon;
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

    // The corner's last spoken action and the lap ordinal it was spoken on — the M32 cross-lap dedup memory.
    private readonly record struct LastCornerTip(string ActionId, int LapOrdinal);
}
