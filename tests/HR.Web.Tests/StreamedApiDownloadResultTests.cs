using System.Net;
using System.Net.Http.Headers;
using HR.Web.Services;
using Microsoft.AspNetCore.Http;

namespace HR.Web.Tests;

public class StreamedApiDownloadResultTests
{
    [Fact]
    public async Task ExecuteAsync_Forwards_Headers_And_Streams_The_Body_Then_Disposes_The_Upstream_Response()
    {
        var source = new TrackingStream(3L * 1024 * 1024);
        var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) };
        upstream.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        upstream.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "export.zip" };
        var body = new MemoryStream();
        var context = new DefaultHttpContext { Response = { Body = body } };

        await new StreamedApiDownloadResult(upstream).ExecuteAsync(context);

        Assert.Equal(3L * 1024 * 1024, body.Length);
        Assert.Equal("application/zip", context.Response.ContentType);
        Assert.Contains("attachment", context.Response.Headers.ContentDisposition.ToString());
        Assert.Contains("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.True(source.Disposed);
        Assert.True(source.MaxReadSize <= 256 * 1024);
    }

    [Fact]
    public async Task ExecuteAsync_Stops_And_Disposes_The_Upstream_When_The_Browser_Disconnects()
    {
        var source = new TrackingStream(10L * 1024 * 1024 * 1024);
        var upstream = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) };
        using var aborted = new CancellationTokenSource();
        var context = new DefaultHttpContext { RequestAborted = aborted.Token, Response = { Body = new AbortingStream(aborted, afterBytes: 1024 * 1024) } };

        await new StreamedApiDownloadResult(upstream).ExecuteAsync(context);

        Assert.True(source.Disposed);
        Assert.True(source.BytesRead < 10L * 1024 * 1024);
    }

    private sealed class TrackingStream(long total) : Stream
    {
        public long BytesRead { get; private set; }
        public int MaxReadSize { get; private set; }
        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => Fill(count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Fill(buffer.Length));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        private int Fill(int requested)
        {
            MaxReadSize = Math.Max(MaxReadSize, requested);
            var n = (int)Math.Min(requested, total - BytesRead);
            BytesRead += n;
            return n;
        }
    }

    private sealed class AbortingStream(CancellationTokenSource aborted, long afterBytes) : Stream
    {
        private long _written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _written; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Track(count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Track(buffer.Length);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        private void Track(int count)
        {
            _written += count;
            if (_written >= afterBytes)
            {
                aborted.Cancel();
            }
        }
    }
}
