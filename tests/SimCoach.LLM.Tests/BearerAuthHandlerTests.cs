using System.Net;
using FluentAssertions;
using SimCoach.LLM.Providers;
using Xunit;

namespace SimCoach.LLM.Tests;

public sealed class BearerAuthHandlerTests
{
    [Fact]
    public async Task Adds_bearer_header_from_env_var_at_send_time()
    {
        string varName = "SIMCOACH_TEST_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(varName, "secret-123");
        try
        {
            var inner = MockHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
            using var client = new HttpClient(new BearerAuthHandler(varName) { InnerHandler = inner });

            using HttpResponseMessage response = await client.GetAsync(new Uri("https://example.test/"));

            inner.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
            inner.LastRequest!.Headers.Authorization!.Parameter.Should().Be("secret-123");
        }
        finally
        {
            Environment.SetEnvironmentVariable(varName, null);
        }
    }

    [Fact]
    public async Task Throws_when_env_var_is_unset()
    {
        string varName = "SIMCOACH_TEST_MISSING_" + Guid.NewGuid().ToString("N");
        var inner = MockHttpMessageHandler.Json(HttpStatusCode.OK, "{}");
        using var client = new HttpClient(new BearerAuthHandler(varName) { InnerHandler = inner });

        Func<Task> act = () => client.GetAsync(new Uri("https://example.test/"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        inner.CallCount.Should().Be(0);
    }
}
