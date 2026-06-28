using System.Globalization;
using System.Resources;

namespace SimCoach.Coach;

/// <summary>
/// Hand-rolled accessor for the embedded <c>CoachStrings.resx</c> (the RU user-facing strings — positional
/// fallback + ordinals). Deliberately <b>not</b> the designer-generated class: that would emit culture-less
/// <c>GetString</c> calls that trip CA1304/CA1305 under <c>TreatWarningsAsErrors</c>. The resx is the neutral
/// (ru-RU) resource set, so we resolve against an explicit ru-RU culture.
/// </summary>
internal static class CoachStrings
{
    private static readonly ResourceManager _resourceManager =
        new("SimCoach.Coach.Resources.CoachStrings", typeof(CoachStrings).Assembly);

    private static readonly CultureInfo _culture = CultureInfo.GetCultureInfo("ru-RU");

    public static string Get(string key) => _resourceManager.GetString(key, _culture) ?? key;
}
