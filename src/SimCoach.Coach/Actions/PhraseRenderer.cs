using System.Globalization;

namespace SimCoach.Coach.Actions;

/// <summary>
/// Fills a <see cref="CoachAction"/>'s RU template from its <see cref="ParamBinding"/>s against an
/// <see cref="IGoldView"/>, applying each <see cref="ParamTransform"/> and unit suffix. All numeric
/// formatting is <see cref="CultureInfo.InvariantCulture"/> — CA1305 is fatal here and a ru-RU current
/// culture would otherwise emit a decimal comma. The first quantitative param becomes the
/// <see cref="RenderedAction.RenderedParam"/> chip value.
/// </summary>
public static class PhraseRenderer
{
    public static RenderedAction Render(CoachAction action, IGoldView gold, CoachOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string phrase = action.PhraseTemplateRu;
        string renderedParam = string.Empty;

        foreach (ParamBinding param in action.Params)
        {
            string value = RenderValue(param, gold, options);
            phrase = phrase.Replace("{" + param.Name + "}", value, StringComparison.Ordinal);

            if (renderedParam.Length == 0 && IsQuantitative(param.Transform))
            {
                renderedParam = value;
            }
        }

        return new RenderedAction(action.Id, action.ActionLabelShort, phrase, renderedParam);
    }

    private static bool IsQuantitative(ParamTransform transform) =>
        transform is ParamTransform.AbsRound0 or ParamTransform.SignedRound0 or ParamTransform.CarLengths;

    private static string RenderValue(ParamBinding param, IGoldView gold, CoachOptions options)
    {
        if (param.Transform == ParamTransform.None)
        {
            if (gold.TryGetString(param.From, out string text))
            {
                return Append(text, param.Unit);
            }

            return gold.TryGetNumber(param.From, out double raw)
                ? Append(raw.ToString(CultureInfo.InvariantCulture), param.Unit)
                : string.Empty;
        }

        if (param.Transform == ParamTransform.ReasonRu)
        {
            return gold.TryGetString(param.From, out string reason)
                ? Append(ReasonGloss.ToRu(reason), param.Unit)
                : string.Empty;
        }

        if (!gold.TryGetNumber(param.From, out double value))
        {
            return string.Empty;
        }

        string formatted = param.Transform switch
        {
            ParamTransform.AbsRound0 =>
                ((long)Math.Round(Math.Abs(value), MidpointRounding.AwayFromZero))
                    .ToString("0", CultureInfo.InvariantCulture),
            ParamTransform.SignedRound0 =>
                ((long)Math.Round(value, MidpointRounding.AwayFromZero))
                    .ToString("+0;-0;0", CultureInfo.InvariantCulture),
            ParamTransform.CarLengths => CarLengthGloss.ToRu(value, options.CarLengthMeters),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

        return Append(formatted, param.Unit);
    }

    private static string Append(string value, string? unit) =>
        string.IsNullOrEmpty(unit) ? value : value + unit;
}
