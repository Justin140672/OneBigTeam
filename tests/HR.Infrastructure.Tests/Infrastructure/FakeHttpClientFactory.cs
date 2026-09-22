namespace HR.Infrastructure.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="IHttpClientFactory"/> test double that always hands back an
/// <see cref="HttpClient"/> wrapping the caller-supplied <see cref="FakeHttpMessageHandler"/>, so the
/// Supabase storage health checks' requests can be inspected without any real network access.
/// </summary>
internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
