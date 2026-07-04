namespace SimCoach.RuEval;

/// <summary>
/// The candidate phrase for one fixture plus a well-formedness flag. <see cref="FormatOk"/> separates a
/// FORMAT failure (malformed / out-of-subset / over-length — the public <c>TipValidator</c> rejected it) from a
/// QUALITY failure (well-formed but the judge scores it below the bar). <see cref="Detail"/> is dumped on a
/// failing run.
/// </summary>
public sealed record CandidateResult(string PhraseRu, bool FormatOk, string Detail);
