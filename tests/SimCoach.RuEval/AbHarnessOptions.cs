namespace SimCoach.RuEval;

/// <summary>
/// The A/B shadow-harness config surface (M30). Tier-2 dev-config, NOT a user slider — it only shapes which
/// one-liner models the advisory comparison fans across and how many judge samples damp the per-fixture score.
/// The owner-decided first cut is gemini-only (<c>ab_gemini_25</c> vs <c>ab_gemini_31</c>, both already
/// registered on <c>openrouter-google</c> with rate cards); the harness MEASURES only and never edits the
/// production route defaults — the routing switch is a separate owner follow-up.
/// </summary>
public sealed record AbHarnessOptions
{
    /// <summary>The candidate route keys the harness fans each fixture across (must resolve to distinct models).</summary>
    public IReadOnlyList<string> Candidates { get; init; } = ["ab_gemini_25", "ab_gemini_31"];

    /// <summary>How many judge calls to average per fixture per candidate to damp nondeterminism (≥ 1).</summary>
    public int SampleCount { get; init; } = 1;

    public void EnsureValid()
    {
        if (Candidates.Count == 0)
        {
            throw new InvalidOperationException("AbHarnessOptions.Candidates must list at least one candidate route.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string candidate in Candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                throw new InvalidOperationException("AbHarnessOptions.Candidates contains an empty route key.");
            }

            if (!seen.Add(candidate))
            {
                throw new InvalidOperationException(
                    $"AbHarnessOptions.Candidates contains a duplicate route key '{candidate}'.");
            }
        }

        if (SampleCount < 1)
        {
            throw new InvalidOperationException("AbHarnessOptions.SampleCount must be at least 1.");
        }
    }
}
