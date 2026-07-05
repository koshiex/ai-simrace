namespace SimCoach.Reference.Tests;

/// <summary>
/// Gate policy for <see cref="GroundTruthRevalidationTests"/>. The revalidation fact skips green when no
/// local fixture is present (bare CI — the MCAP + <c>truth.json</c> never enter the repo), but a PR that
/// mutates the NO-GO-certified line/delta math (M34-populate, M38-linedev) sets
/// <c>SIMCOACH_REQUIRE_GROUNDTRUTH</c> so a missing fixture FAILS instead of skipping green — turning the
/// gate into a recorded merge precondition. Kept as a pure function so the policy is unit-tested without
/// mutating process env, which would race the parallel test host.
/// </summary>
internal static class GroundTruthGate
{
    /// <summary>
    /// True when the require-flag is set to anything other than an explicit <c>0</c>/<c>false</c> — so a
    /// missing fixture must fail rather than skip.
    /// </summary>
    public static bool IsRequired(string? requireFlag) =>
        !string.IsNullOrWhiteSpace(requireFlag)
        && !requireFlag.Equals("0", StringComparison.Ordinal)
        && !requireFlag.Equals("false", StringComparison.OrdinalIgnoreCase);
}
