using HR.Integration.Tests.Infrastructure;
using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Jobs;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// P1 fix (Sept 2026): PurgeOrphanedImportFileUploadsJob's deletion phase must never delete a file
/// out from under a request that just confirmed its upload intent, and must never touch storage for
/// an intent still inside its grace period. HR.Modules.DataImport.Tests/PurgeOrphanedImportFileUploadsJobTests.cs
/// covers this against the EF InMemory provider; this asserts the same guarantee against a real
/// PostgreSQL database (Testcontainers, via ApiWebApplicationFactory) — the optimistic-concurrency
/// token (OrphanedImportFileUpload.Version) depends on real UPDATE ... WHERE version = @p row-count
/// semantics that the InMemory provider only approximates.
///
/// Reaches into the real DataImportDbContext via the test host's DI container and calls the
/// (InternalsVisibleTo-exposed) internal domain methods directly, mirroring
/// ConfirmImportSessionResumabilityEndpointTests' established pattern for simulating interleaving
/// there is no HTTP-level fault-injection hook for.
/// </summary>
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
            Guid.NewGuid(), companyId, storageKey, now.AddHours(-2)); // past the default grace period
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
        // Real interleaving against real Postgres: the job's own DbContext loads a deletion-eligible
        // candidate (as DeleteConfirmedOrphansAsync's SELECT would), a separate request-like context
        // then confirms that same intent (as UploadImportFileHandler's ConfirmedAt save would), and
        // only then does the job attempt its claim step immediately before the destructive storage
        // call. The claim must fail with a genuine Postgres row-version conflict, and the file already
        // uploaded to real storage must never be deleted.
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

        // Simulates the resolution phase of an earlier sweep already having proven this row
        // deletion-eligible, and the job's deletion-phase query having just loaded it (a tracked,
        // still-unconfirmed snapshot) — the exact state DeleteConfirmedOrphansAsync's candidates loop
        // is in immediately before its claim step.
        await using var jobScope = _factory.Services.CreateAsyncScope();
        var jobDb = jobScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var tracked = await jobDb.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
        tracked.MarkDeletionEligible(now.AddHours(-1));
        await jobDb.SaveChangesAsync();

        // A confirming request wins the race, via a fully independent scope/connection/transaction.
        await using (var confirmingScope = _factory.Services.CreateAsyncScope())
        {
            var confirmingDb = confirmingScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
            var confirming = await confirmingDb.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
            confirming.MarkConfirmed(now);
            await confirmingDb.SaveChangesAsync();
        }

        // The job's claim step: touch the row under its concurrency token immediately before the
        // destructive storage call would happen.
        tracked.BeginDeletionAttempt(now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => jobDb.SaveChangesAsync());

        // Storage was never touched — the file the now-confirmed session depends on still exists.
        Assert.True(await storage.ExistsAsync(storageKey, CancellationToken.None));

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<DataImportDbContext>();
        var saved = await verifyDb.OrphanedImportFileUploads.AsNoTracking().SingleAsync(o => o.Id == intent.Id);

        Assert.NotNull(saved.ConfirmedAt);
        Assert.Null(saved.DeletedAt);
    }
}
