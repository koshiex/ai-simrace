using System.Globalization;
using System.Text.RegularExpressions;

namespace SimCoach.Coach;

/// <summary>
/// Pure derivations of corner-name display forms: the positional fallback for an unbaked corner and the
/// spoken RU form (a trailing <c>(N)</c> expanded to an ordinal word). RU building blocks come from
/// <see cref="CoachStrings"/> (the resx), per the "RU user-facing → resx" rule.
/// </summary>
internal static class CornerNameForms
{
    private static readonly Regex _trailingNumber = new(@"_t0*(\d+)$", RegexOptions.Compiled);
    private static readonly Regex _parenSuffix = new(@"\s*\((\d+)\)\s*$", RegexOptions.Compiled);

    /// <summary>Builds "поворот N" from the trailing <c>_tNN</c> of a corner id (N = 0 if it has none).</summary>
    public static string Positional(string cornerId)
    {
        Match match = _trailingNumber.Match(cornerId);
        int number = match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return string.Format(CultureInfo.InvariantCulture, CoachStrings.Get("Corner_Positional_Format"), number);
    }

    /// <summary>
    /// Strips a trailing <c>(N)</c> from a full name and appends its RU ordinal (e.g.
    /// <c>"Raidillon (1)" → "Raidillon, первый"</c>); a name without a <c>(N)</c> is returned unchanged.
    /// </summary>
    public static string Spoken(string fullName)
    {
        Match match = _parenSuffix.Match(fullName);
        if (!match.Success)
        {
            return fullName;
        }

        int number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        string baseName = fullName[..match.Index];
        return $"{baseName}, {CoachStrings.Get($"Ordinal_{number}")}";
    }
}
