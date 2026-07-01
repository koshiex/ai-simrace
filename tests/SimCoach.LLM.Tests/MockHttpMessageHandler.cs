using System.Net;
using System.Text;

namespace SimCoach.LLM.Tests;

/// <summary>
/// Hand-rolled <see cref="HttpMessageHandler"/> test double (no Moq, per repo convention). Captures the last
/// request + body and returns a caller-supplied response, or throws a caller-supplied exception.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastRequestBody { get; private set; }

    public int CallCount { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        if (request.Content is not null)
        {
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return _responder(request);
    }

    public static MockHttpMessageHandler Json(HttpStatusCode status, string body, string? retryAfterSeconds = null)
        => new(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (retryAfterSeconds is not null)
            {
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfterSeconds);
            }

            return response;
        });

    public static MockHttpMessageHandler Throws(Exception exception) => new(_ => throw exception);
}
