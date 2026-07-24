using System.Globalization;
using System.Text.RegularExpressions;

namespace SimCoach.Coach;

/// <summary>
/// Pure derivation of a corner-name display form: the positional fallback ("поворот N") for an unbaked corner.
/// The RU building block comes from <see cref="CoachStrings"/> (the resx), per the "RU user-facing → resx" rule.
/// </summary>
internal static class CornerNameForms
{
    private static readonly Regex _trailingNumber = new(@"_t0*(\d+)$", RegexOptions.Compiled);

    /// <summary>Builds "поворот N" from the trailing <c>_tNN</c> of a corner id (N = 0 if it has none).</summary>
    public static string Positional(string cornerId)
    {
        Match match = _trailingNumber.Match(cornerId);
        int number = match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        return string.Format(CultureInfo.InvariantCulture, CoachStrings.Get("Corner_Positional_Format"), number);
    }
}
