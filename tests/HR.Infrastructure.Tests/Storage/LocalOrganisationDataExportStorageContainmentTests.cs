using HR.Infrastructure.Storage;

namespace HR.Infrastructure.Tests.Storage;

/// <summary>
/// Regression coverage for the storage-containment fix (ticket 8 + follow-up). This service
/// already builds storage keys from GUIDs only (no user-supplied file name is ever
/// incorporated), so these tests focus on the containment check itself.
/// </summary>
public sealed class LocalOrganisationDataExportStorageContainmentTests
{
    private static LocalOrganisationDataExportStorage CreateSut() => new();

    [Fact]
    public async Task UploadAsync_ThenOpenAsync_RoundTripsContent()
    {
        var sut = CreateSut();
        var payload = new byte[] { 9, 8, 7 };
        var companyId = Guid.NewGuid();
        var exportId = Guid.NewGuid();
        var attemptToken = Guid.NewGuid();

        using var upload = new MemoryStream(payload);
        var storageKey = await sut.UploadAsync(companyId, exportId, attemptToken, upload, CancellationToken.None);

        using var buffer = new MemoryStream();
        await using (var stream = await sut.OpenAsync(storageKey, CancellationToken.None))
        {
            Assert.NotNull(stream);
            await stream!.CopyToAsync(buffer);
        }

        Assert.Equal(payload, buffer.ToArray());

        await sut.DeleteAsync(storageKey, CancellationToken.None);
    }

    [Theory]
    [InlineData("../escape.zip")]
    [InlineData("..\\escape.zip")]
    [InlineData("organisation-exports/../../escape.zip")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\escape.zip")]
    public async Task StorageOperations_RejectTraversalAndRootedKeys(string maliciousKey)
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.OpenAsync(maliciousKey, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.DeleteAsync(maliciousKey, CancellationToken.None));
    }

    [Fact]
    public async Task StorageOperations_NeverTouchFilesOutsideStorageRoot()
    {
        var sut = CreateSut();
        var siblingDir = Path.Combine(Path.GetTempPath(), "onebigteam", "organisation-exports-sentinel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(siblingDir);
        var sentinelFile = Path.Combine(siblingDir, "sentinel.zip");
        await File.WriteAllBytesAsync(sentinelFile, "do-not-touch"u8.ToArray());

        try
        {
            var relativeEscape = $"../{Path.GetFileName(siblingDir)}/sentinel.zip";

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
