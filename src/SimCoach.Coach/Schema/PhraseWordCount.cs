namespace SimCoach.Coach.Schema;

/// <summary>
/// Pure word counter for the post-parse phrase-length check. PR-E only tests it; the cadence-aware
/// enforcement (over <c>CoachOptions</c> budgets) lands with the RuleEngine/CoachService in a later PR.
/// </summary>
public static class PhraseWordCount
{
    /// <summary>Counts whitespace-delimited words; empty/whitespace input is zero.</summary>
    public static int Count(string phrase)
    {
        ArgumentNullException.ThrowIfNull(phrase);
        return phrase.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
