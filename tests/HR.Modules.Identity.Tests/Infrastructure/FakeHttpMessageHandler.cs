using System.Net;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;
    public string ResponseBodyToReturn { get; set; } = "{}";

    public TimeSpan? Delay { get; set; }

    public Exception? ExceptionToThrow { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, LastRequestBody));

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
