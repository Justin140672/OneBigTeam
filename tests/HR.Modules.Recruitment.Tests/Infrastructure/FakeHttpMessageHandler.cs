using System.Net;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> test double for exercising
/// SupabaseCandidateDocumentStorageHealthCheck with no network access. Records every request and
/// returns a caller-configured canned response, or a thrown transport exception.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
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
