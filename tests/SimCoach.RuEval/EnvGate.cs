namespace SimCoach.RuEval;

/// <summary>
/// The env-gate for the network path (mirrors <c>GroundTruthRevalidationTests</c>). The gate runs only when
/// <c>SIMCOACH_RU_EVAL</c> is set AND an <c>OPENROUTER_API_KEY</c> is present; otherwise the <c>[Fact]</c>
/// returns early so default <c>dotnet test</c> stays fully offline and needs no key. The pure
/// <see cref="Evaluate"/> is what the always-on hermetic self-test asserts (no real env dependency).
/// </summary>
public static class EnvGate
{
    public const string RuEvalEnvVar = "SIMCOACH_RU_EVAL";
    public const string ApiKeyEnvVar = "OPENROUTER_API_KEY";

    public static bool Evaluate(string? ruEvalFlag, string? apiKey) =>
        !string.IsNullOrWhiteSpace(ruEvalFlag) && !string.IsNullOrWhiteSpace(apiKey);

    public static bool IsEnabled() => Evaluate(
        Environment.GetEnvironmentVariable(RuEvalEnvVar),
        Environment.GetEnvironmentVariable(ApiKeyEnvVar));
}
