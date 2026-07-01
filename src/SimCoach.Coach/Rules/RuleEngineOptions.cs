using SimCoach.Coach.Actions;

namespace SimCoach.Coach.Rules;

/// <summary>
/// Quiet-zone thresholds (FR-035) — all config-driven, no magic numbers in the engine. Mirrors the other
/// Coach options: plain class, <c>init</c> setters, an <see cref="EnsureValid"/> fail-fast. Bound from
/// <c>Coach:Rules</c> as a concrete singleton at composition; <see cref="MonthlyBudgetUsd"/> therefore takes a
/// stored override at startup. A live, no-restart re-bind of the budget arrives with the P5 settings UI.
/// </summary>
public sealed class RuleEngineOptions
{
    /// <summary>Per-cadence minimum gap between tips (<see cref="TimeSpan.Zero"/> = no cooldown).</summary>
    public IReadOnlyDictionary<CoachCadence, TimeSpan> Cooldowns { get; init; } =
        new Dictionary<CoachCadence, TimeSpan>
        {
            [CoachCadence.Corner] = TimeSpan.FromSeconds(4),
            [CoachCadence.Sector] = TimeSpan.FromSeconds(8),
            [CoachCadence.Lap] = TimeSpan.Zero,
            [CoachCadence.Session] = TimeSpan.Zero,
            [CoachCadence.Strategy] = TimeSpan.Zero,
        };

    /// <summary>Suppress when brake + |steer| exceeds this (driver fully loaded). Real-time cadences only.</summary>
    public double WorkloadBrakeSteerSum { get; init; } = 1.6;

    /// <summary>Suppress when |steer-rate| exceeds this (rapid correction). Real-time cadences only.</summary>
    public double WorkloadSteerRate { get; init; } = 4.0;

    /// <summary>On-a-straight gate: |steer| below this AND speed above <see cref="StraightMinSpeedKmh"/>. Real-time only.</summary>
    public double StraightMaxSteer { get; init; } = 0.05;

    /// <summary>On-a-straight gate speed floor (km/h).</summary>
    public double StraightMinSpeedKmh { get; init; } = 150.0;

    /// <summary>
    /// Half-width of the apex quiet-zone band as a fraction of the corner's entry/exit length, used by the
    /// corner-phase resolver (0 &lt; x ≤ 0.5). Larger = a wider apex band where real-time tips are suppressed.
    /// </summary>
    public double ApexWindowFraction { get; init; } = 0.25;

    /// <summary>Normalized-position ranges (0..1) the user marked quiet. Empty by default.</summary>
    public IReadOnlyList<QuietZoneRange> UserQuietZones { get; init; } = [];

    /// <summary>Per-session spend ceiling; at or above it the engine downgrades Speak to TemplateOnly (no LLM).</summary>
    public decimal SessionBudgetUsd { get; init; } = 0.50m;

    /// <summary>
    /// Rolling 30-day spend ceiling (FR-072), fed by <c>ICostQueryRepository.GetRolling30DayCostAsync</c> and
    /// set from the <c>budget.monthly_usd</c> setting. <c>0</c> = no monthly cap (the default until the user
    /// sets one — distinct from the always-on per-session cap).
    /// </summary>
    public decimal MonthlyBudgetUsd { get; init; }

    /// <summary>
    /// Floor on the most-urgent available action: if even the best action in the subset is strictly less
    /// urgent than this, stay silent. Default = the least-urgent possible priority, so it never floors.
    /// </summary>
    public CoachPriority PriorityFloor { get; init; } = new(Enum.GetValues<CoachPhase>()[^1], int.MaxValue);

    public void EnsureValid()
    {
        foreach (CoachCadence cadence in Enum.GetValues<CoachCadence>())
        {
            if (!Cooldowns.TryGetValue(cadence, out TimeSpan cooldown) || cooldown < TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"RuleEngineOptions.Cooldowns is missing or negative for cadence '{cadence}'.");
            }
        }

        if (SessionBudgetUsd <= 0)
        {
            throw new InvalidOperationException("RuleEngineOptions.SessionBudgetUsd must be positive.");
        }

        if (MonthlyBudgetUsd < 0)
        {
            throw new InvalidOperationException("RuleEngineOptions.MonthlyBudgetUsd must be non-negative (0 = no cap).");
        }

        if (ApexWindowFraction is <= 0 or > 0.5)
        {
            throw new InvalidOperationException("RuleEngineOptions.ApexWindowFraction must be in (0, 0.5].");
        }

        foreach (QuietZoneRange zone in UserQuietZones)
        {
            zone.EnsureValid();
        }
    }
}
