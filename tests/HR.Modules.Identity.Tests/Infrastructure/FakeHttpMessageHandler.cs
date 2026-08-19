using System.Net;

namespace HR.Modules.Identity.Tests.Infrastructure;

/// <summary>
/// Minimal test double for <see cref="HttpMessageHandler"/> used to exercise
/// SupabaseAuthGateway's raw-HTTP calls without any real network access. Records the last
/// request sent and returns a caller-configured canned response for every call.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    /// <summary>Every request/body pair seen, in order — for gateway methods that issue more than
    /// one HTTP call (e.g. CreateUserAsync's admin-create followed by a resend), so a test can
    /// assert on a specific step rather than just whichever happened to run last.</summary>
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    public HttpStatusCode StatusCodeToReturn { get; set; } = HttpStatusCode.OK;
    public string ResponseBodyToReturn { get; set; } = "{}";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, LastRequestBody));

        return new HttpResponseMessage(StatusCodeToReturn)
        {
            Content = new StringContent(ResponseBodyToReturn),
        };
    }
}
