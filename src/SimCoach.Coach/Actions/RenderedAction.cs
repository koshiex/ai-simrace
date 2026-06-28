namespace SimCoach.Coach.Actions;

/// <summary>
/// The output of rendering a <see cref="CoachAction"/> against a Gold view: the RU phrase with placeholders
/// filled, plus the authored <see cref="ActionLabelShort"/> chip label and the <see cref="RenderedParam"/>
/// chip value (the quantitative token, incl. sign + unit, e.g. <c>+4м</c>; empty when the action has none).
/// </summary>
public sealed record RenderedAction(
    string ActionId,
    string ActionLabelShort,
    string PhraseRu,
    string RenderedParam);
