namespace SimCoach.Coach;

/// <summary>One entry in <c>cornerNames.json</c>: the full authored name and an optional slim display form.</summary>
internal sealed record CornerNameEntry
{
    public string Name { get; init; } = string.Empty;

    public string? Short { get; init; }
}
