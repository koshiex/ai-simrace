using System.Reflection;
using System.Text.Json.Nodes;
using SimCoach.LLM;

namespace SimCoach.RuEval;

/// <summary>
/// The reference-anchored LLM judge (M18-judge decision). Sends the Gold facts, the committed canonical RU
/// reference phrase, and the candidate <c>phrase_ru</c> to <c>anthropic/claude-sonnet-4.6</c> on the
/// <see cref="RuEvalOptions.JudgeRouteKey"/> route, and parses the strict verdict. Optionally averages
/// <see cref="RuEvalOptions.SampleCount"/> calls to damp nondeterminism. Reuses the same <see cref="ILlmClient"/>
/// seam as candidate generation — no provider-specific type crosses the boundary.
/// </summary>
public sealed class RuJudge
{
    private const string SystemPromptResource = "SimCoach.RuEval.Prompts.ru-judge.system.ru.txt";

    private readonly ILlmClient _llm;
    private readonly RuEvalOptions _options;
    private readonly string _systemPrompt;

    public RuJudge(ILlmClient llm, RuEvalOptions options)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _systemPrompt = ReadSystemPrompt();
    }

    public async Task<IReadOnlyList<JudgeVerdict>> JudgeAsync(
        EvalFixture fixture, string candidatePhrase, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(candidatePhrase);

        LlmRequest request = BuildRequest(fixture, candidatePhrase);
        var verdicts = new List<JudgeVerdict>(_options.SampleCount);
        for (int i = 0; i < _options.SampleCount; i++)
        {
            LlmResult result = await _llm.CompleteAsync(request, ct).ConfigureAwait(false);
            if (result is not LlmResult.Success success)
            {
                var failure = (LlmResult.Failure)result;
                throw new InvalidOperationException($"Judge call for '{fixture.Id}' failed: {failure.Error}");
            }

            if (!VerdictParser.TryParse(success.Json, _options.MaxDimensionScore, out JudgeVerdict? verdict, out string reason))
            {
                throw new InvalidOperationException(
                    $"Judge verdict for '{fixture.Id}' did not parse ({reason}): {success.Json}");
            }

            verdicts.Add(verdict!);
        }

        return verdicts;
    }

    private LlmRequest BuildRequest(EvalFixture fixture, string candidatePhrase)
    {
        JsonNode facts = JsonNode.Parse(fixture.FactsJson) ?? new JsonObject();
        var userPrompt = new JsonObject
        {
            ["gold_facts"] = facts,
            ["reference_phrase_ru"] = fixture.ReferencePhraseRu,
            ["candidate_phrase_ru"] = candidatePhrase,
        };

        return new LlmRequest(
            _options.JudgeRouteKey, _systemPrompt, userPrompt.ToJsonString(), RuJudgeSchema.Verdict(),
            RuJudgeSchema.SchemaName);
    }

    private static string ReadSystemPrompt()
    {
        Assembly assembly = typeof(RuJudge).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(SystemPromptResource)
            ?? throw new InvalidOperationException($"Judge prompt resource '{SystemPromptResource}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
