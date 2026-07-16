namespace SimCoach.Coach.Gold;

/// <summary>
/// One phase band (entry/apex/exit) of the session's per-phase balance trend (M41, proto
/// <c>BalancePhaseTrend</c>). <see cref="Balance"/> is understeer-positive / oversteer-negative in
/// <c>[-1, 1]</c>, same convention as the single <c>understeer_trend</c> scalar but resolved per phase so an
/// entry-oversteer / exit-understeer car is distinguishable. Non-scalar member of
/// <see cref="GoldSessionPayload"/>, so it is excluded from the reflected <c>GoldFieldNames._session</c> drift
/// guard.
/// </summary>
public sealed record GoldBalancePhaseTrend(string Phase, double Balance, int SampleCount);
