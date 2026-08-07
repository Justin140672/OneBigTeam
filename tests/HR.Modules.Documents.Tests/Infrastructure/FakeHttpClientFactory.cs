namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// IHttpClientFactory test double that never touches the network — every request is intercepted
/// by <see cref="StubHttpMessageHandler"/> and answered with canned bytes (or a thrown exception),
/// regardless of the requested URL. Used by ScanUploadedFileJobTests to stand in for the
/// "download the uploaded file, then scan it" step without a real HTTP call.
/// </summary>
internal sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public byte[] ResponseBytes { get; set; } = "file-bytes"u8.ToArray();
    public Exception? ThrowException { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ThrowException is not null)
            return Task.FromException<HttpResponseMessage>(ThrowException);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(ResponseBytes),
        };
        return Task.FromResult(response);
    }
}
