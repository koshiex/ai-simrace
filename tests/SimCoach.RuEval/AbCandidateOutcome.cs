namespace SimCoach.RuEval;

/// <summary>
/// One candidate model's rolled-up scorecard row (M30): the mean quality composite and per-dimension means over
/// the fixtures it produced a well-formed candidate for, its TOTAL ledger cost and MEAN call latency across all
/// attempts, and the format-reject tally. The composite/per-dim means deliberately exclude format-reject rows
/// (a reject has no verdict), while <see cref="FormatRejectRate"/> surfaces them separately — so a model that
/// answers fluently but often malforms is not flattered by a high composite over a shrinking sample.
/// </summary>
public sealed record AbCandidateOutcome(
    string RouteKey,
    string ModelId,
    double Composite,
    double Groundedness,
    double Brevity,
    double NaturalRussian,
    double Actionability,
    double Tone,
    double TotalCostUsd,
    double AvgLatencyMs,
    int JudgedFixtures,
    int FormatRejects)
{
    /// <summary>Total generation attempts (judged + format-rejected) — every one consumed a billable call.</summary>
    public int Calls => JudgedFixtures + FormatRejects;

    /// <summary>Share of attempts the public validator rejected before judging (0 when no attempts were made).</summary>
    public double FormatRejectRate => Calls == 0 ? 0d : (double)FormatRejects / Calls;
}
