namespace SimCoach.Coach.Actions;

/// <summary>
/// One immutable action-registry entry. The valid-subset filter keeps an action when its cadence matches,
/// its reference requirement is met, and every <see cref="When"/> clause holds; the surviving actions are
/// ordered by <see cref="Priority"/>. <see cref="ActionLabelShort"/> is the authored overlay chip label
/// (never a trimmed id); <see cref="HintEn"/> is the authored English one-liner and <see cref="HintRu"/> the
/// authored Russian intent the PromptBuilder injects into the LLM's <c>valid_actions</c> menu (not
/// user-facing); <see cref="Params"/> drive the RU phrase + the rendered chip value. <see cref="HintRu"/> is a
/// separate init member (defaulting empty) so a positional construction stays valid until the loader wires it.
/// </summary>
public sealed record CoachAction(
    string Id,
    string ActionLabelShort,
    CoachCadence Cadence,
    CoachPriority Priority,
    bool RequiresReference,
    IReadOnlyList<WhenClause> When,
    IReadOnlyList<ParamBinding> Params,
    string PhraseTemplateRu,
    string HintEn)
{
    public string HintRu { get; init; } = string.Empty;
}
