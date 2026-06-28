using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SimCoach.Coach.Actions;

/// <summary>
/// The bounded, immutable set of coaching actions loaded from the embedded <c>actionRegistry.json</c>. The
/// loader fails fast on any malformed entry (unknown field/op/phase/transform, duplicate id or priority,
/// dangling template placeholder), so a bad registry crashes at load, never at coaching time.
/// <see cref="ValidSubset"/> returns the cadence-matching, reference-satisfied, clause-passing actions ordered
/// by <see cref="CoachPriority"/> and capped at <c>MaxActionsInMenu</c> — the menu the LLM may select from.
/// </summary>
public sealed class ActionRegistry
{
    private const string SchemaVersion = "actions/1";
    private const string ResourceName = "SimCoach.Coach.Data.actionRegistry.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Regex _placeholderPattern = new(@"\{([^{}]+)\}", RegexOptions.Compiled);

    private ActionRegistry(IReadOnlyList<CoachAction> actions) => Actions = actions;

    public IReadOnlyList<CoachAction> Actions { get; }

    /// <summary>Loads the registry embedded in this assembly.</summary>
    public static ActionRegistry Load()
    {
        Assembly assembly = typeof(ActionRegistry).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded action registry '{ResourceName}' was not found.");
        }

        return LoadFrom(stream);
    }

    /// <summary>Loads and validates a registry from an arbitrary stream (used by tests for malformed input).</summary>
    internal static ActionRegistry LoadFrom(Stream stream)
    {
        ActionRegistryDocument? document =
            JsonSerializer.Deserialize<ActionRegistryDocument>(stream, _jsonOptions);
        if (document is null)
        {
            throw new InvalidOperationException("actionRegistry.json deserialized to null.");
        }

        if (document.SchemaVersion != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"actionRegistry.json schema_version must be '{SchemaVersion}', was '{document.SchemaVersion}'.");
        }

        if (document.Actions is null || document.Actions.Count == 0)
        {
            throw new InvalidOperationException("actionRegistry.json contains no actions.");
        }

        var actions = new List<CoachAction>(document.Actions.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenPriorities = new HashSet<CoachPriority>();

        foreach (ActionEntryDto entry in document.Actions)
        {
            CoachAction action = MapEntry(entry);

            if (!seenIds.Add(action.Id))
            {
                throw new InvalidOperationException($"actionRegistry.json: duplicate action id '{action.Id}'.");
            }

            if (!seenPriorities.Add(action.Priority))
            {
                throw new InvalidOperationException(
                    $"actionRegistry.json: duplicate priority {action.Priority} on action '{action.Id}'.");
            }

            actions.Add(action);
        }

        return new ActionRegistry(actions);
    }

    /// <summary>
    /// The actions valid for <paramref name="gold"/>: cadence matches, the reference requirement is satisfied,
    /// every <c>when</c> clause holds — ordered by <see cref="CoachPriority"/> and capped at
    /// <see cref="CoachOptions.MaxActionsInMenu"/>. An empty result means "stay silent".
    /// </summary>
    public IReadOnlyList<CoachAction> ValidSubset(IGoldView gold, CoachOptions options) =>
    [
        .. Actions
            .Where(a => a.Cadence == gold.Cadence)
            .Where(a => gold.HasReference || !a.RequiresReference)
            .Where(a => a.When.All(clause => ClauseEvaluator.Evaluate(clause, gold)))
            .OrderBy(a => a.Priority)
            .Take(options.MaxActionsInMenu),
    ];

    private static CoachAction MapEntry(ActionEntryDto entry)
    {
        string id = Require(entry.Id, "id", "<unknown>");
        string labelShort = Require(entry.LabelShort, "label_short", id);
        string phraseTemplate = Require(entry.PhraseTemplateRu, "phrase_template_ru", id);
        CoachCadence cadence = MapCadence(entry.Cadence, id);

        if (entry.Priority is null)
        {
            throw new InvalidOperationException($"actionRegistry.json: action '{id}' is missing a priority.");
        }

        var priority = new CoachPriority(MapPhase(entry.Priority.Phase, id), entry.Priority.Rank);
        IReadOnlySet<string> validFields = GoldFieldNames.For(cadence);

        IReadOnlyList<WhenClause> when = (entry.When ?? [])
            .Select(clause => MapClause(clause, id, validFields))
            .ToList();

        IReadOnlyList<ParamBinding> @params = (entry.Params ?? [])
            .Select(param => MapParam(param, id, validFields))
            .ToList();

        ValidatePlaceholders(phraseTemplate, @params, id);

        return new CoachAction(id, labelShort, cadence, priority, entry.RequiresReference, when, @params, phraseTemplate);
    }

    private static WhenClause MapClause(WhenClauseDto clause, string id, IReadOnlySet<string> validFields)
    {
        string field = Require(clause.Field, "when.field", id);
        EnsureField(field, validFields, id);
        ClauseOp op = MapOp(clause.Op, id);

        return clause.Value.ValueKind switch
        {
            JsonValueKind.Number => new WhenClause(field, op, clause.Value.GetDouble(), null),
            JsonValueKind.True => new WhenClause(field, op, null, true),
            JsonValueKind.False => new WhenClause(field, op, null, false),
            _ => throw new InvalidOperationException(
                $"actionRegistry.json: action '{id}' clause on '{field}' has a non-number/bool value."),
        };
    }

    private static ParamBinding MapParam(ParamBindingDto param, string id, IReadOnlySet<string> validFields)
    {
        string name = Require(param.Name, "param.name", id);
        string from = Require(param.From, "param.from", id);
        EnsureField(from, validFields, id);

        return new ParamBinding(name, from, MapTransform(param.Transform, id), param.Unit);
    }

    private static void ValidatePlaceholders(string template, IReadOnlyList<ParamBinding> @params, string id)
    {
        var names = @params.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (Match match in _placeholderPattern.Matches(template))
        {
            string placeholder = match.Groups[1].Value;
            if (!names.Contains(placeholder))
            {
                throw new InvalidOperationException(
                    $"actionRegistry.json: action '{id}' template references '{{{placeholder}}}' with no matching param.");
            }
        }
    }

    private static void EnsureField(string field, IReadOnlySet<string> validFields, string id)
    {
        if (!validFields.Contains(field))
        {
            throw new InvalidOperationException(
                $"actionRegistry.json: action '{id}' references unknown Gold field '{field}'.");
        }
    }

    private static string Require(string? value, string what, string id)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"actionRegistry.json: action '{id}' has an empty '{what}'.");
        }

        return value;
    }

    private static CoachCadence MapCadence(string? cadence, string id) => cadence switch
    {
        "corner" => CoachCadence.Corner,
        "sector" => CoachCadence.Sector,
        "lap" => CoachCadence.Lap,
        _ => throw new InvalidOperationException(
            $"actionRegistry.json: action '{id}' has an unsupported cadence '{cadence}'."),
    };

    private static CoachPhase MapPhase(string? phase, string id) => phase switch
    {
        "brake" => CoachPhase.Brake,
        "entry" => CoachPhase.Entry,
        "apex" => CoachPhase.Apex,
        "exit" => CoachPhase.Exit,
        _ => throw new InvalidOperationException(
            $"actionRegistry.json: action '{id}' has an unknown priority phase '{phase}'."),
    };

    private static ClauseOp MapOp(string? op, string id) => op switch
    {
        "lt" => ClauseOp.Lt,
        "lte" => ClauseOp.Lte,
        "gt" => ClauseOp.Gt,
        "gte" => ClauseOp.Gte,
        "eq" => ClauseOp.Eq,
        "neq" => ClauseOp.Neq,
        "abs_gt" => ClauseOp.AbsGt,
        "abs_lt" => ClauseOp.AbsLt,
        _ => throw new InvalidOperationException(
            $"actionRegistry.json: action '{id}' has an unknown clause op '{op}'."),
    };

    private static ParamTransform MapTransform(string? transform, string id) => transform switch
    {
        null or "none" => ParamTransform.None,
        "abs_round0" => ParamTransform.AbsRound0,
        "signed_round0" => ParamTransform.SignedRound0,
        _ => throw new InvalidOperationException(
            $"actionRegistry.json: action '{id}' has an unknown param transform '{transform}'."),
    };
}
