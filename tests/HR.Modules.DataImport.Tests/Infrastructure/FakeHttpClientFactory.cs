namespace HR.Modules.DataImport.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="IHttpClientFactory"/> test double that always hands back an
/// <see cref="HttpClient"/> wrapping the caller-supplied <see cref="FakeHttpMessageHandler"/>, so
/// SupabaseImportFileStorageHealthCheck's requests can be inspected without any real network access.
/// </summary>
internal sealed class FakeHttpClientFactory(FakeHttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}
