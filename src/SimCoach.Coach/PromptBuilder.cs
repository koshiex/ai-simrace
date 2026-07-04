using System.Text;
using System.Text.Json.Nodes;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Coach.Schema;
using SimCoach.LLM;

namespace SimCoach.Coach;

/// <summary>
/// Turns a deterministic Gold artifact + the valid action subset into a provider-neutral <see cref="LlmRequest"/>:
/// selects the per-cadence system + few-shot prompts, injects the <c>valid_actions</c> menu and
/// <c>phrase_limits</c> into the Gold user message, and compiles the output schema whose <c>action_id</c> enum
/// is exactly the subset. Two branches: real-time (Corner/Sector/Lap — subset-gated) and debrief (Session — no
/// menu; its subset is always empty by design). The LLM library stays cadence-blind (it sees only the route key).
/// </summary>
public sealed class PromptBuilder
{
    private readonly CoachOptions _coachOptions;
    private readonly PromptOptions _promptOptions;

    public PromptBuilder(CoachOptions coachOptions, PromptOptions promptOptions)
    {
        _coachOptions = coachOptions;
        _promptOptions = promptOptions;
    }

    public LlmRequest Build<TEvent>(GoldArtifact<TEvent> gold, CoachCadence cadence, IReadOnlyList<CoachAction> validSubset)
    {
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentNullException.ThrowIfNull(validSubset);

        bool isSession = cadence == CoachCadence.Session;
        bool isRealTime = cadence is CoachCadence.Corner or CoachCadence.Sector or CoachCadence.Lap;
        if (!isRealTime && !isSession)
        {
            throw new InvalidOperationException($"PromptBuilder has no prompt selection for cadence '{cadence}'.");
        }

        if (isRealTime && validSubset.Count == 0)
        {
            throw new InvalidOperationException(
                $"PromptBuilder reached an empty real-time subset for cadence '{cadence}' — the caller must stay silent.");
        }

        // Corner-only abstain (M7): the same gate feeds the schema sentinel and the prompt guidance so the model
        // is only invited to answer "none" when the wire schema actually carries it. One source of truth
        // (CoachOptions.AllowsAbstain) shared with CoachService's post-parse interpretation — no drift.
        bool allowAbstain = isRealTime && _coachOptions.AllowsAbstain(cadence, validSubset[0].Priority);

        // M31: confidence is a global dev-tier flag (not per-request-computed like abstain), requested on every
        // real-time cadence when on. Observe-only — the schema field and prompt guidance never gate the tip.
        bool requestConfidence = isRealTime && _coachOptions.RequestConfidence;

        PromptSelection selection = _promptOptions.For(cadence);
        string systemPrompt = BuildSystemPrompt(cadence, selection, allowAbstain, requestConfidence);
        string userPrompt = BuildUserPrompt(gold, cadence, validSubset, isRealTime);
        string routeKey = _coachOptions.RouteKeys[cadence];

        string jsonSchema;
        string schemaName;
        if (isRealTime)
        {
            jsonSchema = OutputSchema.RealTime([.. validSubset.Select(a => a.Id)], allowAbstain, requestConfidence);
            schemaName = OutputSchema.RealTimeSchemaName;
        }
        else
        {
            jsonSchema = OutputSchema.Debrief(_coachOptions.MaxDebriefLosses);
            schemaName = OutputSchema.DebriefSchemaName;
        }

        return new LlmRequest(routeKey, systemPrompt, userPrompt, jsonSchema, schemaName);
    }

    private static string BuildSystemPrompt(
        CoachCadence cadence, PromptSelection selection, bool allowAbstain, bool requestConfidence)
    {
        string systemText = PromptResources.ReadSystemText(cadence, selection);
        if (allowAbstain)
        {
            systemText += "\n\n" + PromptResources.ReadAbstainGuidance(selection.SystemVersion);
        }

        if (requestConfidence)
        {
            systemText += "\n\n" + PromptResources.ReadConfidenceGuidance(selection.SystemVersion);
        }

        FewShotDocument fewShots = PromptResources.ReadFewShots(selection.FewShotVersion);
        string cadenceKey = CadenceKey(cadence);
        bool isRealTime = cadence is CoachCadence.Corner or CoachCadence.Sector or CoachCadence.Lap;

        var examples = fewShots.Examples!
            .Where(e => !e.Negative && string.Equals(e.Cadence, cadenceKey, StringComparison.Ordinal))
            .ToList();
        if (isRealTime)
        {
            examples.AddRange(fewShots.Examples!.Where(e => e.Negative));
        }

        if (examples.Count == 0)
        {
            return systemText;
        }

        var builder = new StringBuilder(systemText);
        builder.AppendLine();
        builder.AppendLine();
        builder.AppendLine("Примеры:");
        foreach (FewShotExample example in examples)
        {
            builder.AppendLine();
            if (example.Negative)
            {
                string reason = string.IsNullOrWhiteSpace(example.Note) ? string.Empty : $" ({example.Note})";
                builder.AppendLine($"Антипример — так НЕ делай{reason}:");
                builder.AppendLine("Запрос:");
                builder.AppendLine(example.User.GetRawText());
                builder.AppendLine("Неверный ответ:");
                builder.AppendLine(example.Assistant.GetRawText());
            }
            else
            {
                builder.AppendLine("Запрос:");
                builder.AppendLine(example.User.GetRawText());
                builder.AppendLine("Ответ:");
                builder.AppendLine(example.Assistant.GetRawText());
            }
        }

        return builder.ToString();
    }

    private string BuildUserPrompt<TEvent>(
        GoldArtifact<TEvent> gold, CoachCadence cadence, IReadOnlyList<CoachAction> validSubset, bool isRealTime)
    {
        string goldJson = GoldSerializer.Serialize(gold);
        var node = JsonNode.Parse(goldJson);
        if (node is not JsonObject root)
        {
            throw new InvalidOperationException("Serialized Gold artifact is not a JSON object.");
        }

        if (isRealTime)
        {
            var actions = new JsonArray();
            foreach (CoachAction action in validSubset)
            {
                actions.Add(new JsonObject
                {
                    ["id"] = action.Id,
                    ["hint"] = action.HintEn,
                    ["hint_ru"] = action.HintRu,
                });
            }

            root["valid_actions"] = actions;
        }

        root["phrase_limits"] = new JsonObject { ["max_words"] = MaxWords(cadence) };
        return root.ToJsonString();
    }

    private int MaxWords(CoachCadence cadence) => cadence switch
    {
        CoachCadence.Corner => _coachOptions.InCornerMaxWords,
        CoachCadence.Sector => _coachOptions.SectorMaxWords,
        CoachCadence.Lap => _coachOptions.LapMaxWords,
        CoachCadence.Session => _coachOptions.DebriefMaxWords,
        _ => throw new InvalidOperationException($"No word budget for cadence '{cadence}'."),
    };

    private static string CadenceKey(CoachCadence cadence) => cadence switch
    {
        CoachCadence.Corner => "corner",
        CoachCadence.Sector => "sector",
        CoachCadence.Lap => "lap",
        CoachCadence.Session => "session",
        _ => throw new InvalidOperationException($"No cadence key for cadence '{cadence}'."),
    };
}
