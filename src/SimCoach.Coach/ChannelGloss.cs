namespace SimCoach.Coach;

/// <summary>
/// The single RU gloss for a closed-set <c>dominant_channel</c> code (from <c>AggregatedLoss.dominant_channel</c>,
/// M36) — one of the three signed loss channels (<c>brake_point</c>, <c>throttle_resume</c>, <c>min_speed</c>).
/// Sibling to <see cref="ReasonGloss"/>: the debrief renders the dominant CHANNEL (the argmax-scaled deficit)
/// rather than the retained-but-no-longer-authoritative <c>dominant_reason</c>. An empty <em>or unmapped</em>
/// channel falls back to the same neutral "lost time" gloss <see cref="ReasonGloss"/> uses, so a raw channel
/// identifier never reaches voice/overlay.
/// </summary>
internal static class ChannelGloss
{
    private const string NeutralChannelKey = "Reason_slower";

    public static string ToRu(string channel)
    {
        if (string.IsNullOrEmpty(channel))
        {
            return CoachStrings.Get(NeutralChannelKey);
        }

        string key = "Channel_" + channel;
        string gloss = CoachStrings.Get(key);

        // CoachStrings.Get echoes the key back when the resource is missing; degrade an unresolved lookup to
        // the neutral gloss rather than leak the raw channel identifier.
        return string.Equals(gloss, key, StringComparison.Ordinal) ? CoachStrings.Get(NeutralChannelKey) : gloss;
    }
}
