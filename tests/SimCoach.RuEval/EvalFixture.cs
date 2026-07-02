using SimCoach.Coach;
using SimCoach.LLM;

namespace SimCoach.RuEval;

/// <summary>
/// One held-out fixture: a proto-event run through the production Gold → prompt path, its committed canonical RU
/// reference phrase (reference-anchored judging), and — for the two <see cref="KnownBad"/> anchors — a fixed
/// bad candidate phrase judged directly (transliterated corner name / raw-number-in-voice, the M5/M6 failures).
/// <see cref="CandidateRequest"/> is the exact production <see cref="LlmRequest"/> good fixtures generate through;
/// <see cref="FactsJson"/> is that request's user prompt, reused as the judge's Gold-facts context.
/// </summary>
public sealed record EvalFixture(
    string Id,
    CoachCadence Cadence,
    bool HasReference,
    bool KnownBad,
    string ReferencePhraseRu,
    string? CandidatePhraseRu,
    LlmRequest CandidateRequest,
    IReadOnlyList<string> SubsetIds,
    string FactsJson);
