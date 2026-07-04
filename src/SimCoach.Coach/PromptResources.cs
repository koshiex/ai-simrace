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

    private static Assembly Assembly => typeof(PromptResources).Assembly;

    internal static string SystemResourceName(CoachCadence cadence, string version) =>
        cadence == CoachCadence.Session
            ? $"{Prefix}coach.system.debrief.{version}.ru.txt"
            : $"{Prefix}coach.system.{version}.ru.txt";

    internal static string FewShotResourceName(string version) => $"{Prefix}coach.fewshot.{version}.ru.json";

    internal static string RetryReminderResourceName(string version) => $"{Prefix}coach.retry.{version}.ru.txt";

    internal static string AbstainGuidanceResourceName(string version) => $"{Prefix}coach.abstain.{version}.ru.txt";

    /// <summary>The stricter RU reminder appended to a retried prompt (sector/lap/debrief), embedded + versioned.</summary>
    internal static string ReadRetryReminder(string version) => ReadEmbeddedText(RetryReminderResourceName(version));

    /// <summary>The RU abstain rule (M7) appended to the corner system prompt only when abstain is offered.</summary>
    internal static string ReadAbstainGuidance(string version) => ReadEmbeddedText(AbstainGuidanceResourceName(version));

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

            string fewShotName = FewShotResourceName(selection.FewShotVersion);
            if (!manifest.Contains(fewShotName))
            {
                throw new InvalidOperationException($"Embedded prompt resource '{fewShotName}' was not found.");
            }

            // Parse, don't just probe presence: a malformed/empty few-shot must fail the startup self-test
            // (and PR-F's ValidateOnStart), not survive to the first Build.
            _ = ReadFewShots(selection.FewShotVersion);
        }
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
