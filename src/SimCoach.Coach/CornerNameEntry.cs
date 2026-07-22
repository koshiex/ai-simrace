namespace SimCoach.Coach;

/// <summary>
/// One entry in <c>cornerNames.json</c>: the full authored name (Latin, canonical), an optional slim display
/// form (<see cref="Short"/>, overlay chip), and the authored spoken Russian form (<see cref="Ru"/>) the voice
/// path speaks — ordinal word first ("первый Пухон"), full name kept ("Ла-Сурс"), never a glued number.
/// </summary>
internal sealed record CornerNameEntry
{
    public string Name { get; init; } = string.Empty;

    public string? Short { get; init; }

    public string? Ru { get; init; }
}
