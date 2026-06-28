using System.Text.Json.Nodes;

namespace SimCoach.LLM.Providers;

/// <summary>
/// The request-body shaping a provider applies for one schema, produced by an <see cref="ISchemaTranslator"/>.
/// Either <see cref="ResponseFormat"/> is set (json_schema / json_object dialects) or
/// <see cref="Tools"/> + <see cref="ToolChoice"/> are set (forced-tool emulation). <see cref="SystemInstruction"/>
/// is appended to the system message only for the json_object dialect, where the schema cannot ride on the wire.
/// </summary>
internal sealed record SchemaDirective(
    JsonObject? ResponseFormat = null,
    JsonArray? Tools = null,
    JsonObject? ToolChoice = null,
    string? SystemInstruction = null);
