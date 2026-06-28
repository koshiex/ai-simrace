namespace SimCoach.LLM.Providers;

/// <summary>Translates a provider-neutral JSON schema string into one model family's request shaping.</summary>
internal interface ISchemaTranslator
{
    SchemaFamily Family { get; }

    SchemaDirective Translate(string jsonSchema, string schemaName);
}
