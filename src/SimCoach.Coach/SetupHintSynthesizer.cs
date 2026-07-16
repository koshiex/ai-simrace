using System.Globalization;
using SimCoach.Contracts.V1;

namespace SimCoach.Coach;

/// <summary>
/// Synthesizes the grounded session <c>setup_hint</c> (M41, D-SETUPHINT) from the per-phase balance trend —
/// a Gold-layer synthesis, NOT a new proto scalar. Picks the sampled phase band whose absolute balance is
/// greatest and, only when it clears the IOptions threshold, glosses it to a устойчивый снос/занос hint keyed
/// on the phase (ties break toward the earlier entry→apex→exit band). Returns <c>null</c> when there is no
/// balance ground above the threshold, matching the prompt contract that a null hint drops rather than emitting
/// a fabricated "neutral" setup claim. Balance is understeer-positive / oversteer-negative, same convention as
/// <c>understeer_trend</c>.
/// </summary>
internal static class SetupHintSynthesizer
{
    public static string? Synthesize(IReadOnlyList<BalancePhaseTrend> trends, double balanceThreshold)
    {
        ArgumentNullException.ThrowIfNull(trends);

        BalancePhaseTrend? dominant = null;
        foreach (BalancePhaseTrend trend in trends)
        {
            if (trend.SampleCount <= 0)
            {
                continue;
            }

            if (dominant is null || Math.Abs(trend.Balance) > Math.Abs(dominant.Balance))
            {
                dominant = trend;
            }
        }

        if (dominant is null || Math.Abs(dominant.Balance) < balanceThreshold)
        {
            return null;
        }

        string? phaseRu = PhaseRu(dominant.Phase);
        if (phaseRu is null)
        {
            return null;
        }

        string tendencyRu = CoachStrings.Get(dominant.Balance >= 0 ? "SetupHint_Understeer" : "SetupHint_Oversteer");
        return string.Format(CultureInfo.InvariantCulture, CoachStrings.Get("SetupHint_Format"), tendencyRu, phaseRu);
    }

    private static string? PhaseRu(string phase) => phase switch
    {
        "entry" => CoachStrings.Get("SetupHint_Phase_entry"),
        "apex" => CoachStrings.Get("SetupHint_Phase_apex"),
        "exit" => CoachStrings.Get("SetupHint_Phase_exit"),
        _ => null,
    };
}
