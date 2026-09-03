using System.Net;
using System.Text;
using HR.Modules.Documents.Services;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// TEST-004 — Supabase Storage adapter hardening. Upload / sign-url / download failures (non-2xx)
/// must raise and never return a storage key or URL that a caller could persist as "stored OK".
/// Malformed sign-url JSON must fail rather than crash.
/// </summary>
public class SupabaseDocumentStorageServiceTests
{
    private sealed class RoutingHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } =
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(Responder(request));
        }
    }

    private static SupabaseDocumentStorageService Build(RoutingHandler handler) =>
        new(new HttpClient(handler), Options.Create(new SupabaseStorageOptions
        {
            SupabaseUrl = "https://proj.supabase.co",
            ServiceRoleKey = "service-role-secret",
            BucketName = "documents",
            SignedUrlExpirySeconds = 900,
        }));

    private static MemoryStream Content() => new(Encoding.UTF8.GetBytes("the file bytes"));

    // ---- upload -----------------------------------------------------------------------

    [Fact]
    public async Task UploadAsync_Returns_StorageKey_On_Success()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"Key\":\"documents/x\"}") },
        };
        var service = Build(handler);

        var key = await service.UploadAsync(Content(), "contract.pdf", "application/pdf", "hr/contracts", CancellationToken.None);

        Assert.Contains("hr/contracts/", key);
        Assert.EndsWith("/contract.pdf", key);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.StartsWith("https://proj.supabase.co/storage/v1/object/documents/hr/contracts/", request.RequestUri!.ToString());
        Assert.Equal("service-role-secret", request.Headers.Authorization!.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task UploadAsync_NonSuccess_Throws_And_Yields_No_StorageKey(HttpStatusCode status)
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(status) { Content = new StringContent("{\"error\":\"nope\"}") },
        };
        var service = Build(handler);

        string? key = null;
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
            key = await service.UploadAsync(Content(), "c.pdf", "application/pdf", "hr", CancellationToken.None));

        Assert.Null(key); // caller never receives a key it could persist as a successful upload
    }

    [Fact]
    public async Task UploadAsync_Honours_Cancellation()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => throw new TaskCanceledException(),
        };
        var service = Build(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.UploadAsync(Content(), "c.pdf", "application/pdf", "hr", cts.Token));
    }

    // ---- signed download url --------------------------------------------------------

    [Fact]
    public async Task GetDownloadUrlAsync_Builds_Absolute_Url_From_Path_Only_SignedUrl()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"signedURL":"/storage/v1/object/sign/documents/k?token=abc"}"""),
            },
        };
        var service = Build(handler);

        var uri = await service.GetDownloadUrlAsync("documents/k", CancellationToken.None);

        Assert.Equal("https://proj.supabase.co/storage/v1/object/sign/documents/k?token=abc", uri.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetDownloadUrlAsync_NonSuccess_Throws(HttpStatusCode status)
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(status) { Content = new StringContent("{}") },
        };
        var service = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.GetDownloadUrlAsync("documents/k", CancellationToken.None));
    }

    [Fact]
    public async Task GetDownloadUrlAsync_Malformed_Json_Fails_Gracefully()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json at all") },
        };
        var service = Build(handler);

        await Assert.ThrowsAnyAsync<Exception>(
            () => service.GetDownloadUrlAsync("documents/k", CancellationToken.None));
    }

    [Fact]
    public async Task GetDownloadUrlAsync_Missing_SignedUrl_Field_Throws_Not_NullRef_Silently_Returning()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") },
        };
        var service = Build(handler);

        // result! is non-null (empty object deserialises) but SignedUrl is null -> NRE, surfaced not swallowed.
        await Assert.ThrowsAnyAsync<Exception>(
            () => service.GetDownloadUrlAsync("documents/k", CancellationToken.None));
    }

    // ---- open read stream --------------------------------------------------------

    [Fact]
    public async Task OpenReadStreamAsync_Returns_Null_On_404_Not_Throw()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var service = Build(handler);

        Assert.Null(await service.OpenReadStreamAsync("documents/missing", CancellationToken.None));
    }

    [Fact]
    public async Task OpenReadStreamAsync_Throws_On_Other_NonSuccess()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.Forbidden),
        };
        var service = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.OpenReadStreamAsync("documents/k", CancellationToken.None));
    }

    [Fact]
    public async Task OpenReadStreamAsync_Returns_Content_Stream_On_Success()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("hello"u8.ToArray()) },
        };
        var service = Build(handler);

        await using var stream = await service.OpenReadStreamAsync("documents/k", CancellationToken.None);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        Assert.Equal("hello", await reader.ReadToEndAsync());
    }

    // ---- delete -----------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_NonSuccess_Throws()
    {
        var handler = new RoutingHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError),
        };
        var service = Build(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.DeleteAsync("documents/k", CancellationToken.None));
    }
}
