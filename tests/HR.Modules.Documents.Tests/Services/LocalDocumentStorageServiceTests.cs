using HR.Modules.Documents.Services;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Tests.Services;

public sealed class LocalDocumentStorageServiceTests
{
    private static LocalDocumentStorageService CreateSut() => new(new HttpContextAccessor());

    [Fact]
    public async Task OpenReadStreamAsync_ReturnsBytes_ForAnUploadedFile()
    {
        var sut = CreateSut();
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        using var upload = new MemoryStream(payload);
        var storageKey = await sut.UploadAsync(upload, "hello.bin", "application/octet-stream", "test-folder", CancellationToken.None);

        using var buffer = new MemoryStream();
        await using (var stream = await sut.OpenReadStreamAsync(storageKey, CancellationToken.None))
        {
            Assert.NotNull(stream);
            await stream!.CopyToAsync(buffer);
        }

        Assert.Equal(payload, buffer.ToArray());

        await sut.DeleteAsync(storageKey, CancellationToken.None);
    }

    [Fact]
    public async Task OpenReadStreamAsync_ReturnsNull_WhenFileMissing()
    {
        var sut = CreateSut();

        var stream = await sut.OpenReadStreamAsync($"missing/{Guid.NewGuid():N}/nope.bin", CancellationToken.None);

        Assert.Null(stream);
    }
}
