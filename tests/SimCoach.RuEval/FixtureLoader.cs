using System.Reflection;
using System.Text.Json;
using SimCoach.Coach;
using SimCoach.Coach.Actions;
using SimCoach.Coach.Gold;
using SimCoach.Contracts.V1;
using SimCoach.LLM;

namespace SimCoach.RuEval;

/// <summary>
/// Loads the committed proto-event JSON fixtures off the assembly manifest and drives each through the EXACT
/// production build path — <see cref="GoldArtifactBuilder"/> (like <c>GroundTruthRevalidationTests:186-205</c>)
/// then <see cref="PromptBuilder"/> — so the candidate request is byte-identical to runtime. There is no
/// Gold-JSON deserializer; fixtures are proto <see cref="CornerEvent"/>/<see cref="SessionEvent"/> JSON, never
/// pre-built Gold. Regen path (class-doc run-book): edit a <c>Fixtures/*.json</c> wrapper's <c>event</c> block
/// to the new proto shape after any Gold-schema change.
/// </summary>
public static class FixtureLoader
{
    private const string FixturePrefix = "SimCoach.RuEval.Fixtures.";

    public static IReadOnlyList<EvalFixture> Load()
    {
        var coachOptions = new CoachOptions();
        var builder = new GoldArtifactBuilder(CornerNameMap.Load(), coachOptions);
        var promptBuilder = new PromptBuilder(coachOptions, new PromptOptions());
        var registry = ActionRegistry.Load();

        Assembly assembly = typeof(FixtureLoader).Assembly;
        var fixtures = new List<EvalFixture>();
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(FixturePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            fixtures.Add(Build(assembly, resource, builder, promptBuilder, registry, coachOptions));
        }

        if (fixtures.Count == 0)
        {
            throw new InvalidOperationException("No RU-eval fixtures found on the assembly manifest.");
        }

        return [.. fixtures.OrderBy(f => f.Id, StringComparer.Ordinal)];
    }

    private static EvalFixture Build(
        Assembly assembly,
        string resource,
        GoldArtifactBuilder builder,
        PromptBuilder promptBuilder,
        ActionRegistry registry,
        CoachOptions coachOptions)
    {
        using Stream stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Fixture resource '{resource}' could not be opened.");
        using var doc = JsonDocument.Parse(stream);
        JsonElement root = doc.RootElement;

        string id = GetString(root, "id");
        string cadenceText = GetString(root, "cadence");
        bool hasReference = root.GetProperty("hasReference").GetBoolean();
        bool knownBad = root.TryGetProperty("knownBad", out JsonElement kb) && kb.GetBoolean();
        string referencePhrase = GetString(root, "referencePhraseRu");
        string? candidatePhrase = root.TryGetProperty("candidatePhraseRu", out JsonElement cp)
            && cp.ValueKind == JsonValueKind.String
                ? cp.GetString()
                : null;
        string carClass = root.TryGetProperty("carClass", out JsonElement cc) && cc.ValueKind == JsonValueKind.String
            ? cc.GetString()!
            : "gt3";
        string weather = root.TryGetProperty("weatherBucket", out JsonElement wb) && wb.ValueKind == JsonValueKind.String
            ? wb.GetString()!
            : "dry";
        string trackId = root.TryGetProperty("trackId", out JsonElement tk) && tk.ValueKind == JsonValueKind.String
            ? tk.GetString()!
            : string.Empty;
        string eventJson = root.GetProperty("event").GetRawText();

        var ctx = new GoldSessionContext(trackId, carClass, weather, LapNumber: 0, hasReference);

        if (string.Equals(cadenceText, "session", StringComparison.Ordinal))
        {
            SessionEvent session = SessionEvent.Parser.ParseJson(eventJson);
            GoldArtifact<GoldSessionPayload> gold = builder.BuildSession(session, ctx);
            LlmRequest request = promptBuilder.Build(gold, CoachCadence.Session, []);
            return new EvalFixture(
                id, CoachCadence.Session, hasReference, knownBad, referencePhrase, candidatePhrase,
                request, [], request.UserPrompt);
        }

        if (string.Equals(cadenceText, "corner", StringComparison.Ordinal))
        {
            CornerEvent corner = CornerEvent.Parser.ParseJson(eventJson);
            GoldArtifact<GoldCornerEvent> gold = builder.BuildCorner(corner, ctx);
            IReadOnlyList<CoachAction> subset = registry.ValidSubset(new CornerGoldView(gold), coachOptions);
            if (subset.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Fixture '{id}' produced an empty corner subset — runtime would stay silent, nothing to judge.");
            }

            LlmRequest request = promptBuilder.Build(gold, CoachCadence.Corner, subset);
            IReadOnlyList<string> subsetIds = [.. subset.Select(a => a.Id)];
            return new EvalFixture(
                id, CoachCadence.Corner, hasReference, knownBad, referencePhrase, candidatePhrase,
                request, subsetIds, request.UserPrompt);
        }

        throw new InvalidOperationException($"Fixture '{id}' has unsupported cadence '{cadenceText}'.");
    }

    private static string GetString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement el) || el.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Fixture is missing required string field '{name}'.");
        }

        return el.GetString()!;
    }
}
