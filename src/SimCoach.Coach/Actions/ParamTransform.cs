namespace SimCoach.Coach.Actions;

/// <summary>How a <see cref="ParamBinding"/> renders its source Gold value into a phrase/chip token.</summary>
public enum ParamTransform
{
    /// <summary>Render the value verbatim (invariant culture).</summary>
    None,

    /// <summary>Absolute value, rounded to a whole number — drops the sign (e.g. <c>-3.4 → "3"</c>).</summary>
    AbsRound0,

    /// <summary>
    /// Rounded to a whole number, sign preserved with an explicit <c>+</c>/<c>-</c> (e.g. <c>-3.4 → "-3"</c>,
    /// <c>3.6 → "+4"</c>) — the sign is the semantic direction of the correction and is unrecoverable later.
    /// </summary>
    SignedRound0,

    /// <summary>
    /// Glosses a closed-set <c>reason</c> string to its RU phrase via <see cref="ReasonGloss"/> (M21). Unlike
    /// the numeric transforms this is <b>not quantitative</b>: it never populates the overlay's
    /// <see cref="RenderedAction.RenderedParam"/> chip (which stays number-or-nothing).
    /// </summary>
    ReasonRu,
}
