using System.Net;
using HR.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Tests.Storage;

public sealed class SupabaseOrganisationDataExportStorageStreamingTests
{
    private static SupabaseOrganisationDataExportStorage Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Options.Create(new SupabaseOrganisationDataExportStorageOptions
        {
            SupabaseUrl = "https://project.supabase.example",
            ServiceRoleKey = "service-role-secret",
            BucketName = "exports",
        }));

    [Fact]
    public async Task OpenAsync_Returns_Without_Reading_The_Body_And_Streams_It_On_Demand()
    {
        var body = new CountingStream(totalBytes: 512L * 1024 * 1024);
        var handler = new StreamingHandler(HttpStatusCode.OK, body);
        var sut = Create(handler);

        await using var stream = await sut.OpenAsync("organisation-exports/c/e/key.zip", CancellationToken.None);

        Assert.NotNull(stream);
        Assert.Equal(0, body.BytesRead);
        Assert.False(stream!.CanSeek);

        var buffer = new byte[81920];
        var read = await stream.ReadAsync(buffer);

        Assert.True(read > 0);
        Assert.True(body.BytesRead < 1024 * 1024);
        Assert.Equal("Bearer service-role-secret", handler.AuthorizationHeader);
    }

    [Fact]
    public async Task OpenAsync_Dispose_Releases_The_Underlying_Response_Stream()
    {
        var body = new CountingStream(totalBytes: 1024);
        var sut = Create(new StreamingHandler(HttpStatusCode.OK, body));

        var stream = await sut.OpenAsync("organisation-exports/c/e/key.zip", CancellationToken.None);
        await stream!.DisposeAsync();

        Assert.True(body.Disposed);
    }

    [Fact]
    public async Task OpenAsync_Returns_Null_For_A_Missing_Object()
    {
        var sut = Create(new StreamingHandler(HttpStatusCode.NotFound, new CountingStream(0)));

        var stream = await sut.OpenAsync("organisation-exports/c/e/missing.zip", CancellationToken.None);

        Assert.Null(stream);
    }

    [Fact]
    public async Task OpenAsync_Throws_For_Storage_Failures()
    {
        var sut = Create(new StreamingHandler(HttpStatusCode.InternalServerError, new CountingStream(0)));

        await Assert.ThrowsAsync<HttpRequestException>(() => sut.OpenAsync("organisation-exports/c/e/key.zip", CancellationToken.None));
    }

    private sealed class StreamingHandler(HttpStatusCode status, CountingStream body) : HttpMessageHandler
    {
        public string? AuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StreamContent(body) });
        }
    }

    private sealed class CountingStream(long totalBytes) : Stream
    {
        public long BytesRead { get; private set; }
        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, totalBytes - BytesRead);
            BytesRead += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
