using Microsoft.Extensions.Http;

namespace HR.Modules.Identity.Tests.Infrastructure;

internal sealed class FakeHttpClientFactory(FakeHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
