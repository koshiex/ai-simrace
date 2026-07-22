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

    /// <summary>
    /// Converts a metre distance to the intuitive car-length count with the Russian plural noun ("1 корпус",
    /// "2 корпуса") via <see cref="CarLengthGloss"/> — the voice-speakable braking unit that replaces raw metres.
    /// Quantitative: it populates the overlay chip. The car length comes from <see cref="CoachOptions.CarLengthMeters"/>.
    /// </summary>
    CarLengths,
}
