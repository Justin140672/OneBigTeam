using System.Net;

namespace HR.Infrastructure.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> test double for exercising the Infrastructure HTTP
/// adapters (Postmark, etc.) with no network access. Records every request and returns a
/// caller-configured canned response, an optional delay (for cancellation tests) or a thrown
/// transport exception.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1].Request;

    public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;
    public string ResponseBodyToReturn { get; set; } = "{}";
    public TimeSpan? Delay { get; set; }
    public Exception? ExceptionToThrow { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));

        if (Delay is { } delay)
            await Task.Delay(delay, cancellationToken);

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return new HttpResponseMessage(StatusCodeToReturn)
        {
            Content = new StringContent(ResponseBodyToReturn),
        };
    }
}
