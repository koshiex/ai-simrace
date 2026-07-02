using SimCoach.Coach.Actions;

namespace SimCoach.Coach.Rules;

/// <summary>
/// Tier-1 user-facing cadence preferences (UI-ready): the levers a driver would legitimately tune to make
/// the coach more or less talkative. Grouped as one coherent surface (bound under <c>Coach:Rules:Cadence</c>)
/// so a later settings panel can back each knob with a slider ("ползунки"). Every value is validated by
/// <see cref="EnsureValid"/> — no magic numbers reach the engine. High-severity tips bypass all three
/// silence levers below (the never-silent guarantee, enforced in <see cref="RuleEngine"/>), so these knobs
/// only shape the cadence of ordinary tips.
/// </summary>
public sealed class CadenceOptions
{
    /// <summary>
    /// Per-cadence minimum gap between two tips of the <em>same</em> cadence (<see cref="TimeSpan.Zero"/> =
    /// no per-cadence cooldown). The finer lever layered on top of <see cref="GlobalCooldown"/>. Valid range:
    /// each value ≥ <see cref="TimeSpan.Zero"/>; a value must be present for every cadence.
    /// </summary>
    public IReadOnlyDictionary<CoachCadence, TimeSpan> Cooldowns { get; init; } =
        new Dictionary<CoachCadence, TimeSpan>
        {
            [CoachCadence.Corner] = TimeSpan.FromSeconds(4),
            [CoachCadence.Sector] = TimeSpan.FromSeconds(8),
            [CoachCadence.Lap] = TimeSpan.Zero,
            [CoachCadence.Session] = TimeSpan.Zero,
            [CoachCadence.Strategy] = TimeSpan.Zero,
        };

    /// <summary>
    /// Minimum silence between <em>any</em> two spoken tips, across all cadences — stops a corner tip and a
    /// sector tip landing back-to-back. Valid range: ≥ <see cref="TimeSpan.Zero"/> (<see cref="TimeSpan.Zero"/>
    /// = off, rely on the per-cadence <see cref="Cooldowns"/> only). Owner default 3 s.
    /// </summary>
    public TimeSpan GlobalCooldown { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The chattiness cap: the maximum number of tips spoken between two lap boundaries. Guards against a busy
    /// lap firing a "wall of tips". Valid range: &gt; 0. Owner default 5.
    /// </summary>
    public int MaxTipsPerLap { get; init; } = 5;

    /// <summary>
    /// The materiality floor: stay quiet about an event that cost less than this many milliseconds ("don't
    /// bother me under N ms"). Compared against the event's absolute <c>delta_ms</c>; a value of 0 (no measured
    /// loss) fails the floor open. Valid range: ≥ 0 (0 = off). Owner default 100 ms.
    /// </summary>
    public double MinTimeLossMs { get; init; } = 100.0;

    /// <summary>
    /// Which cadences the chattiness levers — the per-lap tip cap (<see cref="MaxTipsPerLap"/>) and the global
    /// cross-cadence cooldown (<see cref="GlobalCooldown"/>) — are allowed to SILENCE. Defaults to
    /// <see cref="CoachCadence.Corner"/> only: a silenced sector- or lap-summary is more jarring than one
    /// dropped corner tip, so sector and lap summaries are exempt from these two levers and stay subject only to
    /// the materiality floor (and their own per-cadence <see cref="Cooldowns"/>). Every spoken tip of any cadence
    /// still counts toward the budget and arms the global cooldown — this set governs only which cadences those
    /// levers can mute. Valid range: any subset of cadences (empty = the cap and global cooldown never silence
    /// anything). The materiality floor is unaffected and applies to all cadences.
    /// </summary>
    public IReadOnlySet<CoachCadence> GovernedCadences { get; init; } =
        new HashSet<CoachCadence> { CoachCadence.Corner };

    public void EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(GovernedCadences);

        foreach (CoachCadence cadence in Enum.GetValues<CoachCadence>())
        {
            if (!Cooldowns.TryGetValue(cadence, out TimeSpan cooldown) || cooldown < TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"CadenceOptions.Cooldowns is missing or negative for cadence '{cadence}'.");
            }
        }

        if (GlobalCooldown < TimeSpan.Zero)
        {
            throw new InvalidOperationException("CadenceOptions.GlobalCooldown must be non-negative (0 = off).");
        }

        if (MaxTipsPerLap <= 0)
        {
            throw new InvalidOperationException("CadenceOptions.MaxTipsPerLap must be positive.");
        }

        if (MinTimeLossMs < 0)
        {
            throw new InvalidOperationException("CadenceOptions.MinTimeLossMs must be non-negative (0 = off).");
        }
    }
}
