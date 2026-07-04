using System.Text.Json;

namespace SimCoach.Coach.Actions;

/// <summary>
/// The snake_case JSON parse target for <c>actionRegistry.json</c>, deserialized with a
/// <see cref="JsonNamingPolicy.SnakeCaseLower"/> policy and mapped to the attribute-free public records by
/// <see cref="ActionRegistry"/>. Kept internal so the serialization shape never reaches the public surface.
/// </summary>
internal sealed record ActionRegistryDocument
{
    public string? SchemaVersion { get; init; }

    public IReadOnlyList<ActionEntryDto>? Actions { get; init; }
}

internal sealed record ActionEntryDto
{
    public string? Id { get; init; }

    public string? LabelShort { get; init; }

    public string? HintEn { get; init; }

    public string? HintRu { get; init; }

    public string? Cadence { get; init; }

    public PriorityDto? Priority { get; init; }

    public bool RequiresReference { get; init; }

    public IReadOnlyList<WhenClauseDto>? When { get; init; }

    public IReadOnlyList<ParamBindingDto>? Params { get; init; }

    public string? PhraseTemplateRu { get; init; }
}

internal sealed record PriorityDto
{
    public string? Phase { get; init; }

    public int Rank { get; init; }
}

internal sealed record WhenClauseDto
{
    public string? Field { get; init; }

    public string? Op { get; init; }

    public JsonElement Value { get; init; }
}

internal sealed record ParamBindingDto
{
    public string? Name { get; init; }

    public string? From { get; init; }

    public string? Transform { get; init; }

    public string? Unit { get; init; }
}
