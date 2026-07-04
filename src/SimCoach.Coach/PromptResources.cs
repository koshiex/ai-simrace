using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SimCoach.Coach;

/// <summary>
/// Loads the embedded, versioned prompt resources for <see cref="PromptBuilder"/>. Manifest names are pinned
/// by an explicit <c>&lt;LogicalName&gt;</c> in the csproj (the <c>.ru.</c> infix would otherwise be parsed by
/// MSBuild <c>AssignCulture</c> as a culture and stripped), so the names built here match verbatim. Mirrors
/// <see cref="Actions.ActionRegistry"/>'s embedded-resource loading.
/// </summary>
internal static class PromptResources
{
    private const string Prefix = "SimCoach.Coach.Prompts.";

    // The retry reminder + retry-reason lines are version-fixed (not per-cadence, so absent from PromptOptions);
    // kept in sync with CoachService.RetryVersion so AssertAllResolve probes the same resource CoachService reads.
    private const string RetryVersion = "v1";

    private static Assembly Assembly => typeof(PromptResources).Assembly;

    internal static string SystemResourceName(CoachCadence cadence, string version) =>
        cadence == CoachCadence.Session
            ? $"{Prefix}coach.system.debrief.{version}.ru.txt"
            : $"{Prefix}coach.system.{version}.ru.txt";

    internal static string FewShotResourceName(string version) => $"{Prefix}coach.fewshot.{version}.ru.json";

    internal static string RetryReminderResourceName(string version) => $"{Prefix}coach.retry.{version}.ru.txt";

    internal static string RetryReasonResourceName(string version) => $"{Prefix}coach.retry-reason.{version}.ru.txt";

    internal static string AbstainGuidanceResourceName(string version) => $"{Prefix}coach.abstain.{version}.ru.txt";

    internal static string ConfidenceGuidanceResourceName(string version) => $"{Prefix}coach.confidence.{version}.ru.txt";

    /// <summary>The stricter RU reminder appended to a retried prompt (sector/lap/debrief), embedded + versioned.</summary>
    internal static string ReadRetryReminder(string version) => ReadEmbeddedText(RetryReminderResourceName(version));

    /// <summary>
    /// The keyed RU refusal-reason lines (M28) whose text <see cref="RetryReasonRu"/> appends to a retry prompt,
    /// embedded + versioned like the retry reminder. Parses the terse <c>key=RU text</c> line format; a blank line
    /// is skipped, a line without a <c>=</c> or an empty resource is a hard error (a missing/typoed key must fail
    /// the startup self-test, not the first retry).
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ReadRetryReasons(string version)
    {
        string text = ReadEmbeddedText(RetryReasonResourceName(version));
        var reasons = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            int separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                throw new InvalidOperationException(
                    $"Retry-reason resource '{RetryReasonResourceName(version)}' has a malformed line: '{line}'.");
            }

            reasons[line[..separator]] = line[(separator + 1)..];
        }

        if (reasons.Count == 0)
        {
            throw new InvalidOperationException(
                $"Retry-reason resource '{RetryReasonResourceName(version)}' has no entries.");
        }

        return reasons;
    }

    /// <summary>The RU abstain rule (M7) appended to the corner system prompt only when abstain is offered.</summary>
    internal static string ReadAbstainGuidance(string version) => ReadEmbeddedText(AbstainGuidanceResourceName(version));

    /// <summary>The RU confidence guidance (M31) appended to the real-time system prompt only when requested.</summary>
    internal static string ReadConfidenceGuidance(string version) => ReadEmbeddedText(ConfidenceGuidanceResourceName(version));

    /// <summary>The system prompt text for a cadence: the on-disk override if set, else the embedded resource.</summary>
    internal static string ReadSystemText(CoachCadence cadence, PromptSelection selection) =>
        string.IsNullOrWhiteSpace(selection.OverridePath)
            ? ReadEmbeddedText(SystemResourceName(cadence, selection.SystemVersion))
            : File.ReadAllText(selection.OverridePath);

    internal static FewShotDocument ReadFewShots(string version)
    {
        string json = ReadEmbeddedText(FewShotResourceName(version));
        FewShotDocument? document = JsonSerializer.Deserialize<FewShotDocument>(json, FewShotJsonOptions);
        if (document?.Examples is null)
        {
            throw new InvalidOperationException(
                $"Few-shot resource '{FewShotResourceName(version)}' has no examples.");
        }

        return document;
    }

    /// <summary>
    /// Self-test: every system + few-shot resource referenced by <paramref name="options"/> resolves (an
    /// override path exists on disk, an embedded resource is in the assembly manifest). Compares against the
    /// real <see cref="Assembly.GetManifestResourceNames"/> set so a stripped/typoed name cannot pass.
    /// </summary>
    internal static void AssertAllResolve(PromptOptions options)
    {
        var manifest = new HashSet<string>(Assembly.GetManifestResourceNames(), StringComparer.Ordinal);

        foreach (CoachCadence cadence in PromptOptions.RealCadences)
        {
            PromptSelection selection = options.For(cadence);

            if (string.IsNullOrWhiteSpace(selection.OverridePath))
            {
                string systemName = SystemResourceName(cadence, selection.SystemVersion);
                if (!manifest.Contains(systemName))
                {
                    throw new InvalidOperationException($"Embedded prompt resource '{systemName}' was not found.");
                }
            }
            else if (!File.Exists(selection.OverridePath))
            {
                throw new InvalidOperationException(
                    $"Prompt override path '{selection.OverridePath}' for cadence '{cadence}' does not exist.");
            }

            // The abstain rule (M7) is versioned off the system version and appended only for corner requests
            // that offer abstain — probe it here so a stripped/typoed name fails the startup self-test, not Build.
            string abstainName = AbstainGuidanceResourceName(selection.SystemVersion);
            if (!manifest.Contains(abstainName))
            {
                throw new InvalidOperationException($"Embedded prompt resource '{abstainName}' was not found.");
            }

            // The confidence guidance (M31) is versioned off the system version and appended only when
            // RequestConfidence is on — probe it here so a stripped/typoed name fails the startup self-test.
            string confidenceName = ConfidenceGuidanceResourceName(selection.SystemVersion);
            if (!manifest.Contains(confidenceName))
            {
                throw new InvalidOperationException($"Embedded prompt resource '{confidenceName}' was not found.");
            }

            string fewShotName = FewShotResourceName(selection.FewShotVersion);
            if (!manifest.Contains(fewShotName))
            {
                throw new InvalidOperationException($"Embedded prompt resource '{fewShotName}' was not found.");
            }

            // Parse, don't just probe presence: a malformed/empty few-shot must fail the startup self-test
            // (and PR-F's ValidateOnStart), not survive to the first Build.
            _ = ReadFewShots(selection.FewShotVersion);
        }

        // Parse the version-fixed retry-reason lines (M28) here too so a stripped/typoed/malformed resource fails
        // the startup self-test (and PromptResourcesTests), not the first retry.
        _ = ReadRetryReasons(RetryVersion);
    }

    private static string ReadEmbeddedText(string resourceName)
    {
        using Stream? stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            throw new InvalidOperationException($"Embedded prompt resource '{resourceName}' was not found.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static JsonSerializerOptions FewShotJsonOptions { get; } = new() { PropertyNameCaseInsensitive = true };
}

/// <summary>The parsed few-shot resource: labeled example request/response pairs (also golden fixtures).</summary>
internal sealed record FewShotDocument
{
    public string? Version { get; init; }

    public IReadOnlyList<FewShotExample>? Examples { get; init; }
}

internal sealed record FewShotExample
{
    public string? Label { get; init; }

    public string? Cadence { get; init; }

    public bool Negative { get; init; }

    public string? Note { get; init; }

    public JsonElement User { get; init; }

    public JsonElement Assistant { get; init; }
}
