namespace SimCoach.RuEval;

/// <summary>
/// One raw A/B measurement: what a single candidate model produced for a single fixture (M30). The judge
/// <see cref="Verdicts"/> (one per <c>SampleCount</c> sample) are present only when the generated phrase cleared
/// the public <c>TipValidator</c> — a malformed / out-of-subset / over-length answer sets <see cref="FormatOk"/>
/// false and carries no verdicts, so the quality average is computed over well-formed candidates only while the
/// reject is still counted. <see cref="CostUsd"/> / <see cref="LatencyMs"/> are read off the per-call
/// <c>llm_usage</c> ledger + call metadata and apply to EVERY generation attempt (a reject still costs a call).
/// The always-on hermetic reducer test hand-builds these; the network <c>[Fact]</c> fills them from real runs.
/// </summary>
public sealed record AbFixtureSample(
    string RouteKey,
    string ModelId,
    string FixtureId,
    bool FormatOk,
    IReadOnlyList<JudgeVerdict> Verdicts,
    double CostUsd,
    double LatencyMs);
