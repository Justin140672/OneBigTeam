using HR.Modules.DataImport.Services;

namespace HR.Modules.DataImport.Tests.Services;

/// <summary>
/// Regression coverage for the storage-containment fix (ticket 8 + follow-up). Runs against the
/// production <see cref="LocalImportFileStorageService"/> and its real temp-directory root
/// (there is no seam to inject a disposable root), scoping every key under a per-test GUID
/// folder so tests never collide, and asserting a sentinel file placed just outside the storage
/// root is never touched by any malicious key.
/// </summary>
public sealed class LocalImportFileStorageServiceTests
{
    private static LocalImportFileStorageService CreateSut() => new();

    private static string BasePath =>
        Path.Combine(Path.GetTempPath(), "onebigteam", "data-import");

    [Fact]
    public async Task UploadAsync_ThenOpenReadAsync_RoundTripsContent()
    {
        var sut = CreateSut();
        var payload = new byte[] { 1, 2, 3, 4, 5 };

        using var upload = new MemoryStream(payload);
        var storageKey = await sut.UploadAsync(upload, "employees.xlsx", "application/octet-stream", $"test-{Guid.NewGuid():N}", CancellationToken.None);

        // The physical key must never carry the original file name through.
        Assert.DoesNotContain("employees.xlsx", storageKey);

        using var buffer = new MemoryStream();
        await using (var stream = await sut.OpenReadAsync(storageKey, CancellationToken.None))
        {
            await stream.CopyToAsync(buffer);
        }

        Assert.Equal(payload, buffer.ToArray());

        await sut.DeleteAsync(storageKey, CancellationToken.None);
        Assert.False(File.Exists(Path.Combine(BasePath, storageKey.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Theory]
    [InlineData("../escape.xlsx")]
    [InlineData("..\\escape.xlsx")]
    [InlineData("folder/../../escape.xlsx")]
    [InlineData("folder\\..\\..\\escape.xlsx")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\escape.xlsx")]
    [InlineData("C:escape.xlsx")]
    [InlineData("folder//escape.xlsx")]
    public async Task StorageOperations_RejectTraversalAndRootedKeys(string maliciousKey)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.GetDownloadUrlAsync(maliciousKey, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.DeleteAsync(maliciousKey, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.OpenReadAsync(maliciousKey, CancellationToken.None));
    }

    [Fact]
    public async Task StorageOperations_RejectCaseDifferingSiblingEscape_OnAnyFilesystem()
    {
        // Even on a case-sensitive filesystem where "DATA-IMPORT" is a different directory from
        // "data-import", the ".." segment is rejected outright before any case comparison
        // happens, so this can never resolve into the sibling directory.
        var sut = CreateSut();
        const string maliciousKey = "../DATA-IMPORT/probe.xlsx";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.OpenReadAsync(maliciousKey, CancellationToken.None));
    }

    [Fact]
    public async Task StorageOperations_NeverTouchFilesOutsideStorageRoot()
    {
        var sut = CreateSut();
        var siblingDir = Path.Combine(Path.GetTempPath(), "onebigteam", "data-import-sentinel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(siblingDir);
        var sentinelFile = Path.Combine(siblingDir, "sentinel.xlsx");
        var originalContent = "do-not-touch"u8.ToArray();
        await File.WriteAllBytesAsync(sentinelFile, originalContent);

        try
        {
            var relativeEscape = $"../{Path.GetFileName(siblingDir)}/sentinel.xlsx";

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.DeleteAsync(relativeEscape, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.OpenReadAsync(relativeEscape, CancellationToken.None));

            Assert.True(File.Exists(sentinelFile));
            Assert.Equal(originalContent, await File.ReadAllBytesAsync(sentinelFile));
        }
        finally
        {
            Directory.Delete(siblingDir, recursive: true);
        }
    }
}
