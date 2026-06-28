namespace SimCoach.LLM.Providers;

/// <summary>Picks the <see cref="ISchemaTranslator"/> for a resolved model id by inferring its family.</summary>
internal interface ISchemaTranslatorSelector
{
    ISchemaTranslator For(string modelId);
}
