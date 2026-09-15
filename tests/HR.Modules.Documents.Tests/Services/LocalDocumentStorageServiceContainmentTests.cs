using HR.Modules.Documents.Services;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Tests.Services;

/// <summary>
/// Regression coverage for the storage-containment fix (ticket 8 + follow-up). See
/// <see cref="HR.Modules.DataImport.Tests.Services.LocalImportFileStorageServiceTests"/> for the
/// rationale behind running against the real temp-directory root scoped by per-test GUIDs.
/// </summary>
public sealed class LocalDocumentStorageServiceContainmentTests
{
    private static LocalDocumentStorageService CreateSut() => new(new HttpContextAccessor());

    [Fact]
    public async Task UploadAsync_NeverIncludesOriginalFileNameInStorageKey()
    {
        var sut = CreateSut();
        using var upload = new MemoryStream([1, 2, 3]);

        var storageKey = await sut.UploadAsync(upload, "../../escape.docx", "application/octet-stream", $"test-{Guid.NewGuid():N}", CancellationToken.None);

        Assert.DoesNotContain("escape", storageKey);
        await sut.DeleteAsync(storageKey, CancellationToken.None);
    }

    [Theory]
    [InlineData("../escape.docx")]
    [InlineData("..\\escape.docx")]
    [InlineData("folder/../../escape.docx")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\escape.docx")]
    [InlineData("C:escape.docx")]
    public async Task StorageOperations_RejectTraversalAndRootedKeys(string maliciousKey)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.OpenReadStreamAsync(maliciousKey, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.DeleteAsync(maliciousKey, CancellationToken.None));
    }

    [Fact]
    public async Task StorageOperations_NeverTouchFilesOutsideStorageRoot()
    {
        var sut = CreateSut();
        var siblingDir = Path.Combine(Path.GetTempPath(), "onebigteam", "documents-sentinel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(siblingDir);
        var sentinelFile = Path.Combine(siblingDir, "sentinel.docx");
        await File.WriteAllBytesAsync(sentinelFile, "do-not-touch"u8.ToArray());

        try
        {
            var relativeEscape = $"../{Path.GetFileName(siblingDir)}/sentinel.docx";

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.DeleteAsync(relativeEscape, CancellationToken.None));

            Assert.True(File.Exists(sentinelFile));
        }
        finally
        {
            Directory.Delete(siblingDir, recursive: true);
        }
    }
}
