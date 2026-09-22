using System.Net;

namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> test double for exercising DocumentStorageHealthCheck
/// with no network access. Distinct from <see cref="StubHttpMessageHandler"/> (used by
/// ScanUploadedFileJobTests for a plain byte download) because the health check needs control over
/// both the status code and the response body. Records every request and returns a
/// caller-configured canned response, or a thrown transport exception.
/// </summary>
internal sealed class FakeSupabaseHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];
    public HttpRequestMessage? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;
    public string ResponseBodyToReturn { get; set; } = "[]";
    public Exception? ExceptionToThrow { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (ExceptionToThrow is not null)
            throw ExceptionToThrow;

        return Task.FromResult(new HttpResponseMessage(StatusCodeToReturn)
        {
            Content = new StringContent(ResponseBodyToReturn),
        });
    }
}
