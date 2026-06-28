namespace SimCoach.LLM.Providers;

/// <summary>
/// Maps a resolved model id to its <see cref="SchemaFamily"/> and the matching translator. Family inference is
/// by OpenRouter slug prefix (<c>anthropic/</c>, <c>google/</c>); everything else defaults to OpenAI-strict.
/// The <see cref="SchemaFamily.JsonObject"/> dialect is registered but never inferred from a slug — it is an
/// explicit fallback reserved for a future config-driven override table.
/// </summary>
internal sealed class SchemaTranslatorSelector : ISchemaTranslatorSelector
{
    private readonly IReadOnlyDictionary<SchemaFamily, ISchemaTranslator> _byFamily;

    public SchemaTranslatorSelector()
        : this(
            new StrictJsonSchemaTranslator(),
            new GeminiSchemaTranslator(),
            new AnthropicToolSchemaTranslator(),
            new JsonObjectSchemaTranslator())
    {
    }

    public SchemaTranslatorSelector(params ISchemaTranslator[] translators)
    {
        ArgumentNullException.ThrowIfNull(translators);
        var map = new Dictionary<SchemaFamily, ISchemaTranslator>();
        foreach (ISchemaTranslator translator in translators)
        {
            map[translator.Family] = translator;
        }

        _byFamily = map;
    }

    public ISchemaTranslator For(string modelId)
    {
        SchemaFamily family = FamilyOf(modelId);
        if (!_byFamily.TryGetValue(family, out ISchemaTranslator? translator))
        {
            throw new InvalidOperationException($"No schema translator registered for family '{family}'.");
        }

        return translator;
    }

    public static SchemaFamily FamilyOf(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        if (modelId.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase))
        {
            return SchemaFamily.AnthropicTool;
        }

        if (modelId.StartsWith("google/", StringComparison.OrdinalIgnoreCase))
        {
            return SchemaFamily.Gemini;
        }

        return SchemaFamily.OpenAiStrict;
    }
}
