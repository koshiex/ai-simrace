namespace SimCoach.RuEval;

/// <summary>
/// One judge verdict: the five per-dimension scores (each 0..<c>MaxDimensionScore</c>) plus a short RU
/// justification. Parsed off the strict verdict schema by <see cref="VerdictParser"/>; folded into a composite
/// by <see cref="ScoreAggregator"/>.
/// </summary>
public sealed record JudgeVerdict(
    int Groundedness,
    int Brevity,
    int NaturalRussian,
    int Actionability,
    int Tone,
    string JustificationRu);
