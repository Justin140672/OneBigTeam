using HR.Modules.Recruitment.Services;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Tests.Services;

/// <summary>
/// Regression coverage for the storage-containment fix (ticket 8 + follow-up). See
/// <see cref="HR.Modules.DataImport.Tests.Services.LocalImportFileStorageServiceTests"/> for the
/// rationale behind running against the real temp-directory root scoped by per-test GUIDs.
/// </summary>
public sealed class LocalCandidateDocumentStorageServiceContainmentTests
{
    private static LocalCandidateDocumentStorageService CreateSut() => new(new HttpContextAccessor());

    [Fact]
    public async Task UploadAsync_NeverIncludesOriginalFileNameInStorageKey()
    {
        var sut = CreateSut();
        using var upload = new MemoryStream([1, 2, 3]);

        var storageKey = await sut.UploadAsync(upload, "../../escape.pdf", "application/pdf", $"test-{Guid.NewGuid():N}", CancellationToken.None);

        Assert.DoesNotContain("escape", storageKey);
        await sut.DeleteAsync(storageKey, CancellationToken.None);
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("..\\escape.pdf")]
    [InlineData("folder/../../escape.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\escape.pdf")]
    public async Task DeleteAsync_RejectsTraversalAndRootedKeys(string maliciousKey)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.DeleteAsync(maliciousKey, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_NeverTouchesFilesOutsideStorageRoot()
    {
        var sut = CreateSut();
        var siblingDir = Path.Combine(Path.GetTempPath(), "onebigteam", "recruitment", "candidate-documents-sentinel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(siblingDir);
        var sentinelFile = Path.Combine(siblingDir, "sentinel.pdf");
        await File.WriteAllBytesAsync(sentinelFile, "do-not-touch"u8.ToArray());

        try
        {
            var relativeEscape = $"../{Path.GetFileName(siblingDir)}/sentinel.pdf";

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
