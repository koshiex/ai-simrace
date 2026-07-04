using Microsoft.Extensions.Options;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.LLM;

namespace SimCoach.Coach;

/// <summary>
/// The Coach-typed half of the B3 <c>ValidateOnStart</c> checklist (registered in <c>AddCoaching</c> at PR-H):
/// #2 route/cadence completeness, #4 registry-field-vs-Gold, #6 prompt-resource existence. Split from
/// <c>LlmStartupValidator</c> because these checks need Coach-only types (<see cref="ActionRegistry"/>,
/// <see cref="GoldView"/>, <c>PromptResources</c>) that <c>SimCoach.LLM</c> cannot reference.
/// </summary>
public sealed class CoachStartupValidator : IValidateOptions<CoachOptions>
{
    private static readonly CoachCadence[] _registryCadences =
        [CoachCadence.Corner, CoachCadence.Sector, CoachCadence.Lap];

    private readonly IOptions<LlmOptions> _llmOptions;
    private readonly IOptions<PromptOptions> _promptOptions;
    private readonly ActionRegistry _registry;

    public CoachStartupValidator(
        IOptions<LlmOptions> llmOptions,
        IOptions<PromptOptions> promptOptions,
        ActionRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(llmOptions);
        ArgumentNullException.ThrowIfNull(promptOptions);
        ArgumentNullException.ThrowIfNull(registry);
        _llmOptions = llmOptions;
        _promptOptions = promptOptions;
        _registry = registry;
    }

    public ValidateOptionsResult Validate(string? name, CoachOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidateRouteCadenceCompleteness(options, failures);
        ValidateDebriefRouteFamily(options, failures);
        ValidateRegistryFieldsAgainstGold(failures);
        ValidatePromptResources(failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // M28 — hard-fail when the debrief route resolves to a Gemini-family model. Gemini's responseSchema strips
    // maxItems (GeminiSchemaTranslator), so the debrief's bounded top_losses would ride unconstrained on the
    // wire and lean entirely on the post-parse TipValidator cap — an unwanted per-family robustness gap on the
    // one long structured payload. Keyed on the family inference, not a hardcoded model list.
    private void ValidateDebriefRouteFamily(CoachOptions options, List<string> failures)
    {
        if (!options.RouteKeys.TryGetValue(CoachCadence.Session, out string? routeKey)
            || string.IsNullOrWhiteSpace(routeKey)
            || !_llmOptions.Value.Routes.TryGetValue(routeKey, out RouteOptions? route))
        {
            return; // completeness check above already reports a missing/unresolved debrief route.
        }

        if (ModelSchemaFamilyGuard.IsGeminiFamily(route.ModelId))
        {
            failures.Add(
                $"Debrief route '{routeKey}' resolves to Gemini-family model '{route.ModelId}', whose "
                + "responseSchema strips maxItems; pick a non-Gemini model for the debrief (structured) route.");
        }
    }

    // #2 — every cadence (incl. reserved Strategy) maps to a route key that resolves to a registered provider.
    private void ValidateRouteCadenceCompleteness(CoachOptions options, List<string> failures)
    {
        LlmOptions llm = _llmOptions.Value;
        foreach (CoachCadence cadence in Enum.GetValues<CoachCadence>())
        {
            if (!options.RouteKeys.TryGetValue(cadence, out string? routeKey) || string.IsNullOrWhiteSpace(routeKey))
            {
                failures.Add($"Coach cadence '{cadence}' has no route key.");
                continue;
            }

            if (!llm.Routes.TryGetValue(routeKey, out RouteOptions? route))
            {
                failures.Add($"Route key '{routeKey}' (cadence '{cadence}') is not configured in LlmOptions.Routes.");
                continue;
            }

            if (!llm.Providers.ContainsKey(route.ProviderId))
            {
                failures.Add(
                    $"Route '{routeKey}' references provider '{route.ProviderId}' not in LlmOptions.Providers.");
            }
        }
    }

    // #4 — every action's when/param field resolves through the REAL per-cadence Gold view (so the registry's
    // static GoldFieldNames catalog cannot drift from the GoldArtifact record shape).
    private void ValidateRegistryFieldsAgainstGold(List<string> failures)
    {
        foreach (CoachCadence cadence in _registryCadences)
        {
            IGoldView view = SampleView(cadence);
            IEnumerable<CoachAction> actions = _registry.Actions.Where(a => a.Cadence == cadence);
            foreach (CoachAction action in actions)
            {
                IEnumerable<string> fields = action.When.Select(w => w.Field)
                    .Concat(action.Params.Select(p => p.From))
                    .Distinct(StringComparer.Ordinal);
                foreach (string field in fields)
                {
                    if (!Resolves(view, field))
                    {
                        failures.Add(
                            $"Action '{action.Id}' references Gold field '{field}' that does not resolve for cadence '{cadence}'.");
                    }
                }
            }
        }
    }

    // #6 — every referenced per-cadence system/few-shot resource resolves (embedded or override path).
    private void ValidatePromptResources(List<string> failures)
    {
        try
        {
            PromptResources.AssertAllResolve(_promptOptions.Value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException or IOException)
        {
            failures.Add($"Prompt resources do not resolve: {ex.Message}");
        }
    }

    private static bool Resolves(IGoldView view, string field)
        => view.TryGetNumber(field, out _) || view.TryGetBool(field, out _) || view.TryGetString(field, out _);

    // Fully-populated artifacts (has_reference=true, all reference-relative fields non-null) so every catalog
    // field is resolvable — the check confirms the static field set matches the live record shape.
    private static IGoldView SampleView(CoachCadence cadence) => cadence switch
    {
        CoachCadence.Corner => GoldView.For(new GoldArtifact<GoldCornerEvent>(
            "gold/1", "corner", "ru-RU",
            SampleSession(),
            new GoldCornerEvent("spa_t1", "Eau Rouge", 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, false, "low_min_speed"))),
        CoachCadence.Sector => GoldView.For(new GoldArtifact<GoldSectorEvent>(
            "gold/1", "sector", "ru-RU",
            SampleSession(),
            new GoldSectorEvent(0, 30000, 1, "Eau Rouge", []))),
        CoachCadence.Lap => GoldView.For(new GoldArtifact<GoldLapEvent>(
            "gold/1", "lap", "ru-RU",
            SampleSession(),
            new GoldLapEvent(1, 90000, 1, true, true, "Eau Rouge", new GoldThermalSummary(80, 400, false, false), []))),
        _ => throw new NotSupportedException($"No registry actions for cadence '{cadence}'."),
    };

    private static GoldSessionBlock SampleSession() => new("spa", "gt3", "dry", 1, true);
}
