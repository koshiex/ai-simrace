using SimCoach.Contracts.V1;
using SimCoach.Pipeline.Kernels;

namespace SimCoach.Reference;

/// <summary>
/// Rolls per-corner per-phase balance up across a session into the three <see cref="BalancePhaseTrend"/>
/// bands (entry/apex/exit, M41). Fed each accumulable corner's <see cref="PhaseBalanceScores"/> by
/// <see cref="ComputeSession"/>; mirrors the session-scalar <c>understeer_trend(11)</c> formula but resolved
/// per phase, so an entry-oversteer / exit-understeer car is distinguishable. Balance is understeer-positive
/// / oversteer-negative in <c>[-1, 1]</c>. Mutation is isolated here; <see cref="Build"/> returns an
/// immutable snapshot — empty until at least one corner is accumulated.
/// </summary>
internal sealed class PhaseBalanceAccumulator
{
    private readonly PhaseAccumulator _entry = new();
    private readonly PhaseAccumulator _apex = new();
    private readonly PhaseAccumulator _exit = new();

    public void Accept(PhaseBalanceScores scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        _entry.Add(scores.Entry);
        _apex.Add(scores.Apex);
        _exit.Add(scores.Exit);
    }

    /// <summary>One entry per phase band in fixed entry→apex→exit order; empty when no corner accumulated.</summary>
    public IReadOnlyList<BalancePhaseTrend> Build()
    {
        if (_entry.CornerCount == 0)
        {
            return [];
        }

        return
        [
            _entry.ToTrend("entry"),
            _apex.ToTrend("apex"),
            _exit.ToTrend("exit"),
        ];
    }

    private sealed class PhaseAccumulator
    {
        private double _understeerSum;
        private double _oversteerSum;

        public int CornerCount { get; private set; }

        public void Add(BalanceScores scores)
        {
            _understeerSum += scores.UndersteerScore;
            _oversteerSum += scores.OversteerScore;
            CornerCount++;
        }

        public BalancePhaseTrend ToTrend(string phase) => new()
        {
            Phase = phase,
            Balance = CornerCount > 0
                ? Math.Clamp((float)((_understeerSum - _oversteerSum) / CornerCount), -1f, 1f)
                : 0f,
            SampleCount = CornerCount,
        };
    }
}
