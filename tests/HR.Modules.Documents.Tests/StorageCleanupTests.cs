using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Documents.Tests;

public class StorageCleanupTests
{
    [Fact]
    public async Task TryDeleteAsync_Deletes_Object_And_Logs_Nothing_On_Success()
    {
        var storage = new FakeDocumentStorageService();
        var logger  = new FakeLogger<StorageCleanupTests>();

        await StorageCleanup.TryDeleteAsync(storage, logger, "Op", "a/b.pdf", Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(["a/b.pdf"], storage.Deletions);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task TryDeleteAsync_Does_Not_Throw_And_Logs_Error_With_Safe_Identifiers_When_Delete_Fails()
    {
        var storage   = new FakeDocumentStorageService { ThrowOnDelete = true };
        var logger    = new FakeLogger<StorageCleanupTests>();
        var companyId = Guid.NewGuid();
        var entityId  = Guid.NewGuid();

        await StorageCleanup.TryDeleteAsync(storage, logger, "UploadEmployeeDocument", "co/emp/key.pdf", companyId, entityId);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.IsType<InvalidOperationException>(entry.Exception);
        Assert.Contains("UploadEmployeeDocument", entry.Message);
        Assert.Contains("co/emp/key.pdf", entry.Message);
        Assert.Contains(companyId.ToString(), entry.Message);
        Assert.Contains(entityId.ToString(), entry.Message);
    }

    [Fact]
    public async Task TryDeleteAsync_ProfilePhoto_Overload_Does_Not_Throw_And_Logs_When_Delete_Fails()
    {
        var storage = new FakeProfilePhotoStorageService { ThrowOnDelete = true };
        var logger  = new FakeLogger<StorageCleanupTests>();

        await StorageCleanup.TryDeleteAsync(storage, logger, "Op", "photo/key.png", Guid.NewGuid(), null);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("photo/key.png", entry.Message);
    }

    [Fact]
    public async Task TryDeleteAsync_Tolerates_A_Missing_Logger()
    {
        var storage = new FakeDocumentStorageService { ThrowOnDelete = true };

        var ex = await Record.ExceptionAsync(() =>
            StorageCleanup.TryDeleteAsync(storage, null, "Op", "k", Guid.NewGuid(), null));

        Assert.Null(ex);
    }

    [Fact]
    public async Task TryDeleteAsync_Ignores_A_Cancelled_Caller_Token_Because_Cleanup_Must_Still_Run()
    {
        // The helper takes no caller token by design: the usual reason the surrounding operation failed
        // is cancellation, and a cancelled token would make the compensating delete fail as well.
        var storage = new FakeDocumentStorageService();

        await StorageCleanup.TryDeleteAsync(storage, null, "Op", "k", Guid.NewGuid(), null);

        Assert.Equal(["k"], storage.Deletions);
    }
}
