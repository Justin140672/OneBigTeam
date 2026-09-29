using HR.Integration.Tests.Infrastructure;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Jobs;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class PurgeOrphanedImportFileUploadsJobConcurrencyTests
{
    private readonly ApiWebApplicationFactory _factory;

    public PurgeOrphanedImportFileUploadsJobConcurrencyTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Expired_Intent_With_An_Object_In_Real_Storage_Is_Deleted_End_To_End()
    {
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var storage = seedScope.ServiceProvider.GetRequiredService<IImportFileStorageService>();

        var storageKey = storage.GenerateStorageKey($"{companyId}", "orphan-e2e.xlsx");
        await using (var content = new MemoryStream([1, 2, 3]))
        {
            await storage.UploadAsync(content, storageKey, "application/octet-stream", CancellationToken.None);
        }

        var intent = OrphanedImportFileUpload.CreateReserved(
            Guid.NewGuid(), companyId, storageKey, now.AddHours(-2));
        seedDb.OrphanedImportFileUploads.Add(intent);
        await seedDb.SaveChangesAsync();

        await using var jobScope = _factory.Services.CreateAsyncScope();
        var job = jobScope.ServiceProvider.GetRequiredService<PurgeOrphanedImportFileUploadsJob>();
        await job.ExecuteAsync();

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var saved = await verifyDb.OrphanedImportFileUploads.AsNoTracking().SingleAsync(o => o.Id == intent.Id);

        Assert.NotNull(saved.DeletionEligibleAt);
        Assert.NotNull(saved.DeletedAt);

        var storageAfter = verifyScope.ServiceProvider.GetRequiredService<IImportFileStorageService>();
        await Assert.ThrowsAnyAsync<Exception>(
            () => storageAfter.OpenReadAsync(storageKey, CancellationToken.None));
    }

    [Fact]
    public async Task A_Request_Confirming_Its_Intent_After_The_Job_Has_Loaded_It_Wins_The_Race_And_The_File_Survives()
    {
        var companyId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var seedScope = _factory.Services.CreateAsyncScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var storage = seedScope.ServiceProvider.GetRequiredService<IImportFileStorageService>();

        var storageKey = storage.GenerateStorageKey($"{companyId}", "orphan-race.xlsx");
        await using (var content = new MemoryStream([4, 5, 6]))
        {
            await storage.UploadAsync(content, storageKey, "application/octet-stream", CancellationToken.None);
        }

        var intent = OrphanedImportFileUpload.CreateReserved(
            Guid.NewGuid(), companyId, storageKey, now.AddHours(-2));
        seedDb.OrphanedImportFileUploads.Add(intent);
        await seedDb.SaveChangesAsync();

        await using var jobScope = _factory.Services.CreateAsyncScope();
        var jobDb = jobScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var tracked = await jobDb.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
        tracked.MarkDeletionEligible(now.AddHours(-1));
        await jobDb.SaveChangesAsync();

        await using (var confirmingScope = _factory.Services.CreateAsyncScope())
        {
            var confirmingDb = confirmingScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
            var confirming = await confirmingDb.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
            confirming.MarkConfirmed(now);
            await confirmingDb.SaveChangesAsync();
        }

        tracked.BeginDeletionAttempt(now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => jobDb.SaveChangesAsync());

        Assert.True(await storage.ExistsAsync(storageKey, CancellationToken.None));

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var saved = await verifyDb.OrphanedImportFileUploads.AsNoTracking().SingleAsync(o => o.Id == intent.Id);

        Assert.NotNull(saved.ConfirmedAt);
        Assert.Null(saved.DeletedAt);
    }
}
