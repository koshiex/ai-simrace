using SimCoach.Coach;
using SimCoach.LLM;

namespace SimCoach.RuEval;

/// <summary>
/// Produces the candidate RU phrase for a fixture. Good fixtures generate through the resolved live
/// <see cref="ILlmClient"/> on the production route and are gated by the PUBLIC
/// <see cref="TipValidator.TryValidateRealtime"/> / <see cref="TipValidator.TryValidateDebrief"/> (must-fix e) so
/// a malformed answer reports as a FORMAT failure, never a quality one. The two known-bad anchors bypass
/// generation — their fixed bad candidate is judged directly to prove the scale still rejects it.
/// </summary>
public static class CandidateSource
{
    public static async Task<CandidateResult> GenerateAsync(
        EvalFixture fixture, ILlmClient llm, CoachOptions coachOptions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(coachOptions);

        if (fixture.KnownBad)
        {
            string injected = fixture.CandidatePhraseRu ?? string.Empty;
            return new CandidateResult(injected, !string.IsNullOrWhiteSpace(injected), "known-bad injected candidate");
        }

        LlmResult result = await llm.CompleteAsync(fixture.CandidateRequest, ct).ConfigureAwait(false);
        if (result is not LlmResult.Success success)
        {
            var failure = (LlmResult.Failure)result;
            return new CandidateResult(string.Empty, false, $"llm failure: {failure.Error}");
        }

        if (fixture.Cadence == CoachCadence.Session)
        {
            bool ok = TipValidator.TryValidateDebrief(
                success.Json, coachOptions.MaxDebriefLosses, coachOptions.DebriefMaxWords, out string topPriority,
                out string debriefFailure);
            return ok
                ? new CandidateResult(topPriority, true, "debrief top_priority")
                : new CandidateResult(success.Json, false, $"format reject: {debriefFailure}");
        }

        RealtimeTipVerdict verdict = TipValidator.TryValidateRealtime(
            success.Json, fixture.SubsetIds, coachOptions.InCornerMaxWords, allowAbstain: false,
            out string actionId, out string phrase, out string realtimeFailure, out _);
        return verdict == RealtimeTipVerdict.Accept
            ? new CandidateResult(phrase, true, $"action_id={actionId}")
            : new CandidateResult(success.Json, false, $"format {verdict}: {realtimeFailure}");
    }
}
