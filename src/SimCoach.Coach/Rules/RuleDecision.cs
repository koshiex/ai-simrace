namespace SimCoach.Coach.Rules;

/// <summary>What the rule engine decided for one candidate tip.</summary>
public enum RuleOutcome
{
    /// <summary>Run the LLM, then validate / template downstream.</summary>
    Speak,

    /// <summary>Emit nothing — a quiet zone fired.</summary>
    Silent,

    /// <summary>Emit a template-only tip (no LLM call) — e.g. over the session budget.</summary>
    TemplateOnly,
}

/// <summary>The specific quiet-zone (or non-) reason behind a <see cref="RuleDecision"/>.</summary>
public enum QuietReason
{
    None,
    EmptySubset,
    Cooldown,
    Workload,
    Straight,
    ApexWindow,
    RecentContact,
    RecentOffTrack,
    UserZone,
    SessionNotGreen,
    StrategyReserved,
    OverBudget,
    PriorityFloor,

    /// <summary>The event's measured time-loss is below <see cref="CadenceOptions.MinTimeLossMs"/> (materiality floor).</summary>
    BelowTimeLossFloor,

    /// <summary>A tip was spoken within <see cref="CadenceOptions.GlobalCooldown"/> (cross-cadence silence).</summary>
    GlobalCooldown,

    /// <summary>The per-lap tip cap (<see cref="CadenceOptions.MaxTipsPerLap"/>) is spent for this lap.</summary>
    LapTipBudget,

    /// <summary>
    /// The same advice for the same corner was already spoken within
    /// <see cref="CadenceOptions.RepeatSuppressionLaps"/> laps (or already this lap) — cross-lap dedup (M32).
    /// </summary>
    RepeatSuppressed,
}

/// <summary>The rule-engine verdict for a candidate tip: an outcome plus the reason that drove it.</summary>
public readonly record struct RuleDecision(RuleOutcome Outcome, QuietReason Reason)
{
    public static RuleDecision Speak { get; } = new(RuleOutcome.Speak, QuietReason.None);

    public static RuleDecision Silent(QuietReason reason) => new(RuleOutcome.Silent, reason);

    public static RuleDecision TemplateOnly(QuietReason reason) => new(RuleOutcome.TemplateOnly, reason);
}
