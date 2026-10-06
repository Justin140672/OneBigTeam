namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public byte[] ResponseBytes { get; set; } = "file-bytes"u8.ToArray();
    public Exception? ThrowException { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        if (ThrowException is not null)
            return Task.FromException<HttpResponseMessage>(ThrowException);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(ResponseBytes),
        };
        return Task.FromResult(response);
    }
}
