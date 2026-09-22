using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Tests.Infrastructure;

internal static class TestFile
{
    // Magic-byte headers for the content types used across these tests, so the real
    // SupportAttachmentValidator's signature check (ticket 4, P1) accepts them by default.
    private static readonly Dictionary<string, byte[]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = [0x89, 0x50, 0x4E, 0x47],
        ["image/jpeg"] = [0xFF, 0xD8, 0xFF, 0xE0],
        ["application/pdf"] = [0x25, 0x50, 0x44, 0x46],
    };

    public static IFormFile Create(
        string fileName = "screenshot.png", string contentType = "image/png", int size = 128,
        bool validSignature = true)
    {
        var content = new byte[size];
        Array.Fill(content, (byte)0x1);

        if (validSignature && Signatures.TryGetValue(contentType, out var signature))
            signature.CopyTo(content, 0);

        return new FormFile(new MemoryStream(content), 0, content.Length, "Files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    public static IFormFileCollection Collection(params IFormFile[] files)
    {
        var collection = new FormFileCollection();
        collection.AddRange(files);
        return collection;
    }

    /// <summary>Reliability review issue 4 (P1): a form file whose stream throws partway through
    /// <c>CopyToAsync</c>, simulating a client disconnect / truncated upload mid-copy.</summary>
    public static IFormFile CreateWithThrowingStream(
        string fileName = "broken.png", string contentType = "image/png") =>
        new ThrowingStreamFormFile(fileName, contentType);

    private sealed class ThrowingStreamFormFile(string fileName, string contentType) : IFormFile
    {
        public string ContentType { get; set; } = contentType;
        public string ContentDisposition { get; set; } = string.Empty;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length => 128;
        public string Name => "Files";
        public string FileName => fileName;

        public Stream OpenReadStream() => new ThrowingStream();

        public void CopyTo(Stream target) => throw new IOException("Simulated stream-copy failure.");

        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) =>
            throw new IOException("Simulated stream-copy failure.");

        private sealed class ThrowingStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => 128;
            public override long Position { get; set; }

            public override int Read(byte[] buffer, int offset, int count) =>
                throw new IOException("Simulated stream-copy failure.");

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                throw new IOException("Simulated stream-copy failure.");

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
