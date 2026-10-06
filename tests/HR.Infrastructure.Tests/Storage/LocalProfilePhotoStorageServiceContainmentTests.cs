using HR.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Infrastructure.Tests.Storage;

/// <summary>
/// Regression coverage for the storage-containment fix (ticket 8 + follow-up). See
/// <see cref="HR.Modules.DataImport.Tests.Services.LocalImportFileStorageServiceTests"/> for the
/// rationale behind running against the real temp-directory root scoped by per-test GUIDs.
/// </summary>
public sealed class LocalProfilePhotoStorageServiceContainmentTests
{
    private static LocalProfilePhotoStorageService CreateSut() =>
        new(new HttpContextAccessor(), new ServiceCollection().BuildServiceProvider(), new HR.Infrastructure.Abstractions.LocalStorageUrlSigner(TimeProvider.System));

    [Fact]
    public async Task UploadAsync_NeverIncludesOriginalFileNameInStorageKey()
    {
        var sut = CreateSut();
        using var upload = new MemoryStream([1, 2, 3]);

        var storageKey = await sut.UploadAsync(upload, "../../escape.png", "image/png", $"test-{Guid.NewGuid():N}", CancellationToken.None);

        Assert.DoesNotContain("escape", storageKey);
        await sut.DeleteAsync(storageKey, CancellationToken.None);
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("..\\escape.png")]
    [InlineData("folder/../../escape.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\escape.png")]
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
        var siblingDir = Path.Combine(Path.GetTempPath(), "onebigteam", "profile-photos-sentinel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(siblingDir);
        var sentinelFile = Path.Combine(siblingDir, "sentinel.png");
        await File.WriteAllBytesAsync(sentinelFile, "do-not-touch"u8.ToArray());

        try
        {
            var relativeEscape = $"../{Path.GetFileName(siblingDir)}/sentinel.png";

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => sut.DeleteAsync(relativeEscape, CancellationToken.None));

            Assert.True(File.Exists(sentinelFile));
        }
        finally
        {
            Directory.Delete(siblingDir, recursive: true);
        }
    }

    [Fact]
    public async Task PromoteToCleanAsync_Copies_Quarantined_File_To_A_Clean_Key_Readable_Through_OpenReadAsync()
    {
        var sut = CreateSut();
        var folder = HR.Infrastructure.Abstractions.ProfilePhotoStorageKeys.QuarantineFolder($"test-{Guid.NewGuid():N}");
        using var upload = new MemoryStream([4, 5, 6]);
        var quarantineKey = await sut.UploadAsync(upload, "photo.png", "image/png", folder, CancellationToken.None);

        var cleanKey = await sut.PromoteToCleanAsync(quarantineKey, CancellationToken.None);

        Assert.StartsWith("clean/", cleanKey);
        await using (var stream = await sut.OpenReadAsync(cleanKey, CancellationToken.None))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            Assert.Equal([4, 5, 6], buffer.ToArray());
        }

        await sut.DeleteAsync(quarantineKey, CancellationToken.None);
        await Assert.ThrowsAsync<FileNotFoundException>(() => sut.OpenReadAsync(quarantineKey, CancellationToken.None));
        await sut.DeleteAsync(cleanKey, CancellationToken.None);
    }
}
