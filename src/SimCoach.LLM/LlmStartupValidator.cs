using Microsoft.Extensions.Options;

namespace SimCoach.LLM;

/// <summary>
/// The LlmOptions-only half of the B3 <c>ValidateOnStart</c> checklist (registered in <c>AddLlm</c> at PR-H):
/// #1 cost-meter rate coverage, #3 fallback-route acyclicity, #5 positive timeouts/max-tokens. The Coach-typed
/// checks (#2 route/cadence completeness, #4 registry-field-vs-Gold, #6 prompt-resource existence) live in
/// <c>CoachStartupValidator</c> because <c>SimCoach.LLM</c> cannot reference <c>SimCoach.Coach</c>.
/// </summary>
public sealed class LlmStartupValidator : IValidateOptions<LlmOptions>
{
    public ValidateOptionsResult Validate(string? name, LlmOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var failures = new List<string>();

        ValidateRateCoverage(options, failures);
        ValidateFallbackAcyclicity(options, failures);
        ValidateTimeoutsAndTokens(options, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // #1 — every route's (providerId, modelId) has a configured rate (input + output + cached are all on ModelRate).
    private static void ValidateRateCoverage(LlmOptions options, List<string> failures)
    {
        foreach ((string key, RouteOptions route) in options.Routes)
        {
            if (!options.Providers.TryGetValue(route.ProviderId, out ProviderOptions? provider))
            {
                failures.Add($"Route '{key}' references provider '{route.ProviderId}' that is not configured.");
                continue;
            }

            if (!provider.Rates.ContainsKey(route.ModelId))
            {
                failures.Add(
                    $"Route '{key}' has no rate for model '{route.ModelId}' under provider '{route.ProviderId}'.");
            }
        }
    }

    // #3 — the FallbackRouteKey graph has no cycle and no dangling target.
    private static void ValidateFallbackAcyclicity(LlmOptions options, List<string> failures)
    {
        foreach (string start in options.Routes.Keys)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? current = start;
            while (current is not null)
            {
                if (!visited.Add(current))
                {
                    failures.Add($"Route fallback chain starting at '{start}' contains a cycle.");
                    break;
                }

                if (!options.Routes.TryGetValue(current, out RouteOptions? route))
                {
                    failures.Add($"Route fallback chain starting at '{start}' targets unconfigured route '{current}'.");
                    break;
                }

                current = route.FallbackRouteKey;
            }
        }
    }

    // #5 — positive max-tokens and a timeout of at least 100 ms on every route.
    private static void ValidateTimeoutsAndTokens(LlmOptions options, List<string> failures)
    {
        foreach ((string key, RouteOptions route) in options.Routes)
        {
            if (route.MaxOutputTokens <= 0)
            {
                failures.Add($"Route '{key}' MaxOutputTokens must be positive.");
            }

            if (route.Timeout < TimeSpan.FromMilliseconds(100))
            {
                failures.Add($"Route '{key}' Timeout must be at least 100 ms.");
            }
        }
    }
}
