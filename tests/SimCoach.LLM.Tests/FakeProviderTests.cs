using FluentAssertions;
using SimCoach.LLM;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class FakeProviderTests
{
    private const string RealTimeSchema =
        """{"type":"object","properties":{"action_id":{"type":"string","enum":["wider_entry","brake_later_by_meters"]},"phrase_ru":{"type":"string"}}}""";

    // Captured verbatim from one deterministic run (System.Text.Json escapes the Cyrillic phrase_ru to
    // \uXXXX) — an independent golden, so a silent change to the echo shape or property order fails here.
    private const string ExpectedRealtimeJson =
        """{"action_id":"wider_entry","phrase_ru":"\u0422\u043E\u0440\u043C\u043E\u0437\u0438 \u043F\u043E\u0437\u0436\u0435."}""";

    private static readonly ResolvedRoute _route =
        new("openrouter-google", "google/gemini-2.5-flash-lite", 96, TimeSpan.FromSeconds(2), ReasoningEffort.Off, false);

    private static LlmRequest Request(string schema, string schemaName = "coach_tip") =>
        new("corner", "system", "user", schema, schemaName);

    [Fact]
    public async Task Echoes_first_action_id_enum_from_schema()
    {
        var provider = new FakeProvider();

        LlmResult result = await provider.CompleteAsync(Request(RealTimeSchema), _route, CancellationToken.None);

        result.Should().BeOfType<LlmResult.Success>()
            .Which.Json.Should().Contain("\"action_id\":\"wider_entry\"").And.Contain("phrase_ru");
    }

    [Fact]
    public async Task Is_byte_deterministic_across_calls()
    {
        var provider = new FakeProvider();
        LlmRequest request = Request(RealTimeSchema);

        var first = (LlmResult.Success)await provider.CompleteAsync(request, _route, CancellationToken.None);
        var second = (LlmResult.Success)await provider.CompleteAsync(request, _route, CancellationToken.None);

        second.Json.Should().Be(first.Json);
        second.Usage.Should().Be(first.Usage);
    }

    [Fact]
    public async Task Reports_provider_and_model_from_resolved_route()
    {
        var provider = new FakeProvider();

        var result = (LlmResult.Success)await provider.CompleteAsync(Request(RealTimeSchema), _route, CancellationToken.None);

        result.Info.ProviderId.Should().Be("openrouter-google");
        result.Info.ProviderModelId.Should().Be("google/gemini-2.5-flash-lite");
        result.Info.Latency.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Returns_minimal_fixture_for_schema_without_action_enum()
    {
        var provider = new FakeProvider();
        const string debriefSchema = """{"type":"object","properties":{"top_priority":{"type":"string"}}}""";

        var result = (LlmResult.Success)await provider.CompleteAsync(Request(debriefSchema, "debrief"), _route, CancellationToken.None);

        result.Json.Should().Be("""{"schema":"debrief"}""");
    }

    [Fact]
    public async Task Does_not_throw_on_malformed_schema()
    {
        var provider = new FakeProvider();

        var result = (LlmResult.Success)await provider.CompleteAsync(Request("not json", "debrief"), _route, CancellationToken.None);

        result.Json.Should().Be("""{"schema":"debrief"}""");
    }

    [Fact]
    public async Task Does_not_throw_on_non_string_action_enum()
    {
        var provider = new FakeProvider();
        const string numericEnumSchema = """{"type":"object","properties":{"action_id":{"enum":[123]}}}""";

        var result = (LlmResult.Success)await provider.CompleteAsync(Request(numericEnumSchema, "debrief"), _route, CancellationToken.None);

        result.Json.Should().Be("""{"schema":"debrief"}""");
    }

    [Fact]
    public async Task Realtime_fixture_is_byte_and_usage_pinned()
    {
        var provider = new FakeProvider();

        var result = (LlmResult.Success)await provider.CompleteAsync(Request(RealTimeSchema), _route, CancellationToken.None);

        // Hardcoded so the estimator (System "system"+user "user" → 2 input; 116-char echo → 29 output)
        // is pinned independently, not re-derived from FakeProvider's own length/4 formula.
        result.Json.Should().Be(ExpectedRealtimeJson);
        result.Usage.Should().Be(new LlmUsage(2, 29));
    }

    [Fact]
    public void StreamAsync_throws_not_supported()
    {
        var provider = new FakeProvider();

        Action act = () => provider.StreamAsync(Request(RealTimeSchema), _route, CancellationToken.None);

        act.Should().Throw<NotSupportedException>();
    }
}
