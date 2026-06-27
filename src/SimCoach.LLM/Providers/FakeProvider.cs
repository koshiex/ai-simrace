using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimCoach.LLM.Providers;

/// <summary>
/// Deterministic, network-free CI/test default. Echoes the schema's first <c>action_id</c> enum member
/// (so the real-time output schema is always satisfied) with a fixed RU phrase; a schema without an
/// <c>action_id</c> enum (e.g. debrief) or a malformed schema yields a minimal deterministic echo. Latency
/// is zero and token counts are a pure function of length, so it doubles as a byte-stable cost fixture.
/// </summary>
internal sealed class FakeProvider : ILlmProvider
{
    private const string DefaultPhraseRu = "Тормози позже.";

    public Task<LlmResult> CompleteAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
    {
        string json = BuildFixtureJson(request);
        var usage = new LlmUsage(
            EstimateTokens(request.SystemPrompt) + EstimateTokens(request.UserPrompt),
            EstimateTokens(json));
        var info = new LlmCallInfo(route.ProviderId, route.ModelId, TimeSpan.Zero, "stop");
        return Task.FromResult<LlmResult>(new LlmResult.Success(json, usage, info));
    }

    public IAsyncEnumerable<LlmDelta> StreamAsync(LlmRequest request, ResolvedRoute route, CancellationToken ct)
        => throw new NotSupportedException("FakeProvider streaming is declared for P6, not wired in Phase 3.");

    private static string BuildFixtureJson(LlmRequest request)
    {
        string? firstAction = TryReadFirstActionEnum(request.JsonSchema);
        JsonObject echo = firstAction is not null
            ? new JsonObject { ["action_id"] = firstAction, ["phrase_ru"] = DefaultPhraseRu }
            : new JsonObject { ["schema"] = request.SchemaName };
        return echo.ToJsonString();
    }

    private static string? TryReadFirstActionEnum(string jsonSchema)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonSchema);
            if (doc.RootElement.TryGetProperty("properties", out JsonElement props)
                && props.TryGetProperty("action_id", out JsonElement actionId)
                && actionId.TryGetProperty("enum", out JsonElement enumArr)
                && enumArr.ValueKind == JsonValueKind.Array
                && enumArr.GetArrayLength() > 0)
            {
                JsonElement first = enumArr[0];
                return first.ValueKind == JsonValueKind.String ? first.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Malformed schema → fall through to the minimal fixture rather than throwing.
        }

        return null;
    }

    private static int EstimateTokens(string text) => text.Length / 4;
}
