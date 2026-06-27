using FluentAssertions;
using SimCoach.LLM;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class ProviderOptionsTests
{
    private static ProviderOptions Valid() => new()
    {
        BaseUrl = "https://openrouter.ai/api/v1",
        AuthEnvVar = "OPENROUTER_API_KEY",
        Rates = new Dictionary<string, ModelRate>
        {
            ["google/gemini-2.5-flash-lite"] = new() { InputPerMillion = 0.10m, OutputPerMillion = 0.40m },
        },
    };

    [Fact]
    public void Valid_options_pass()
        => Valid().Invoking(o => o.EnsureValid()).Should().NotThrow();

    [Fact]
    public void Relative_base_url_throws()
        => (Valid() with { BaseUrl = "/api/v1" }).Invoking(o => o.EnsureValid())
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Non_http_scheme_base_url_throws()
        => (Valid() with { BaseUrl = "ftp://openrouter.ai/api" }).Invoking(o => o.EnsureValid())
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Empty_auth_env_var_throws()
        => (Valid() with { AuthEnvVar = "" }).Invoking(o => o.EnsureValid())
            .Should().Throw<InvalidOperationException>();

    [Fact]
    public void Empty_rate_model_id_throws()
    {
        ProviderOptions options = Valid() with
        {
            Rates = new Dictionary<string, ModelRate> { [" "] = new() { InputPerMillion = 1m } },
        };

        options.Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Recurses_into_a_bad_rate()
    {
        ProviderOptions options = Valid() with
        {
            Rates = new Dictionary<string, ModelRate> { ["m"] = new() { InputPerMillion = -1m } },
        };

        options.Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }
}
