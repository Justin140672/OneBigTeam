using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Jobs;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Tests;

public class PurgeOrphanedImportFileUploadsJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 9, 0, 0, TimeSpan.Zero);

    private static readonly DataImportFileRetentionOptions Retention = new()
    {
        AbandonedSessionRetentionDays = 7,
        RetryGraceHours = 1,
        ExhaustedAttemptThreshold = 3,
    };

    private static readonly DateTimeOffset InsideRetryGrace = Now.AddMinutes(-30);
    private static readonly DateTimeOffset PastRetryGrace = Now.AddHours(-2);

    private static DataImportDbContext BuildContext(string databaseName)
    {
        var builder = new DbContextOptionsBuilder<DataImportDbContext>();
        builder.UseVersionedAggregates().UseInMemoryDatabase(databaseName);
        return new DataImportDbContext(builder.Options);
    }

    private static PurgeOrphanedImportFileUploadsJob BuildJob(
        DataImportDbContext db,
        FakeImportFileStorageService storage,
        FakeLegalHoldStatusReader? legalHold = null,
        FakeAdministrativeAlertWriter? alerts = null,
        DataImportFileRetentionOptions? retention = null) =>
        new(
            db,
            storage,
            Options.Create(retention ?? Retention),
            legalHold ?? new FakeLegalHoldStatusReader(),
            alerts ?? new FakeAdministrativeAlertWriter(),
            new FakeClock(Now.UtcDateTime),
            NullLogger<PurgeOrphanedImportFileUploadsJob>.Instance);

    private static OrphanedImportFileUpload CreateOrphan(
        Guid companyId, string storageKey, DateTimeOffset createdAt) =>
        OrphanedImportFileUpload.CreateReserved(Guid.NewGuid(), companyId, storageKey, createdAt);

    [Fact]
    public async Task Pending_Orphan_With_No_Prior_Attempt_Has_Its_File_Deleted()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var orphan = CreateOrphan(companyId, "companies/c1/upload.xlsx", Now.AddDays(-1));
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Contains(orphan.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == orphan.Id);
        Assert.NotNull(saved.DeletedAt);
    }

    [Fact]
    public async Task Successful_Delete_Marks_DeletedAt()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var orphan = CreateOrphan(companyId, "companies/c2/upload.xlsx", PastRetryGrace);
        orphan.RecordAttemptFailed(PastRetryGrace);
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == orphan.Id);
        Assert.NotNull(saved.DeletedAt);
        Assert.Equal(1, saved.AttemptCount);
    }

    [Fact]
    public async Task Failed_Delete_Increments_AttemptCount_And_Does_Not_Throw()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var orphan = CreateOrphan(companyId, "companies/c3/upload.xlsx", Now.AddDays(-1));
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.DoesNotContain(orphan.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == orphan.Id);
        Assert.Null(saved.DeletedAt);
        Assert.Equal(1, saved.AttemptCount);
        Assert.NotNull(saved.LastAttemptedAt);
    }

    [Fact]
    public async Task Failure_Inside_The_Retry_Grace_Window_Is_Not_Retried_Until_Grace_Elapses()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var orphan = CreateOrphan(companyId, "companies/c4/upload.xlsx", Now.AddDays(-1));
        orphan.RecordAttemptFailed(InsideRetryGrace);
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == orphan.Id);
        Assert.Null(saved.DeletedAt);
        Assert.Equal(1, saved.AttemptCount);
    }

    [Fact]
    public async Task Failure_Past_The_Retry_Grace_Window_Is_Retried()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var orphan = CreateOrphan(companyId, "companies/c5/upload.xlsx", Now.AddDays(-1));
        orphan.RecordAttemptFailed(PastRetryGrace);
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Contains(orphan.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == orphan.Id);
        Assert.NotNull(saved.DeletedAt);
    }

    [Fact]
    public async Task Attempt_Count_Reaching_The_Exhausted_Threshold_Raises_An_Administrative_Alert()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var alerts = new FakeAdministrativeAlertWriter();
        var orphan = CreateOrphan(companyId, "companies/c6/upload.xlsx", Now.AddDays(-1));
        orphan.RecordAttemptFailed(PastRetryGrace);
        orphan.RecordAttemptFailed(PastRetryGrace);
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage, alerts: alerts).ExecuteAsync();
        }

        var raised = Assert.Single(alerts.Raised);
        Assert.Equal(companyId, raised.CompanyId);
        Assert.Equal(HR.Infrastructure.Abstractions.AdministrativeAlertCategory.Compliance, raised.Category);
        Assert.Equal(orphan.Id, raised.AffectedEntityId);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == orphan.Id);
        Assert.Equal(3, saved.AttemptCount);
        Assert.Null(saved.DeletedAt);
    }

    [Fact]
    public async Task Attempt_Count_Below_The_Exhausted_Threshold_Does_Not_Raise_An_Administrative_Alert()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var alerts = new FakeAdministrativeAlertWriter();
        var orphan = CreateOrphan(companyId, "companies/c7/upload.xlsx", PastRetryGrace);
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage, alerts: alerts).ExecuteAsync();
        }

        Assert.Empty(alerts.Raised);
    }

    [Fact]
    public async Task Orphan_Under_Legal_Hold_Is_Skipped_And_Never_Attempted()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var legalHold = new FakeLegalHoldStatusReader(companyId);
        var orphan = CreateOrphan(companyId, "companies/c8/upload.xlsx", Now.AddDays(-1));
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage, legalHold: legalHold).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == orphan.Id);
        Assert.Null(saved.DeletedAt);
        Assert.Equal(0, saved.AttemptCount);
    }

    [Fact]
    public async Task Running_ExecuteAsync_Twice_Against_The_Same_Eligible_Orphan_Only_Deletes_Once()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var orphan = CreateOrphan(companyId, "companies/c9/upload.xlsx", Now.AddDays(-1));
        storage.SeedContent(orphan.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Single(storage.Deletions);
        Assert.Equal(orphan.StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task Orphan_Whose_File_Is_Already_Deleted_Is_Not_A_Candidate()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var orphan = CreateOrphan(companyId, "companies/c10/upload.xlsx", Now.AddDays(-1));
        orphan.MarkDeleted(Now.AddDays(-1));

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(orphan);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);
    }


    [Fact]
    public async Task Recent_Reserved_Intent_With_An_Existing_Object_Is_Not_Deleted()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var intent = CreateOrphan(companyId, "companies/c11/upload.xlsx", Now.AddMinutes(-1));
        storage.SeedContent(intent.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(intent);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
        Assert.Null(saved.DeletedAt);
        Assert.Null(saved.ClearedAt);
        Assert.Null(saved.DeletionEligibleAt);
    }

    [Fact]
    public async Task Recent_Reserved_Intent_Without_An_Object_Is_Not_Cleared()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var intent = CreateOrphan(companyId, "companies/c12/upload.xlsx", Now.AddMinutes(-1));

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(intent);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
        Assert.Null(saved.ClearedAt);
        Assert.Null(saved.DeletionEligibleAt);
    }

    [Fact]
    public async Task Expired_Intent_With_An_Object_Is_Deleted()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var intent = CreateOrphan(companyId, "companies/c13/upload.xlsx", Now.AddHours(-2));
        storage.SeedContent(intent.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(intent);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Contains(intent.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
        Assert.NotNull(saved.DeletionEligibleAt);
        Assert.NotNull(saved.DeletedAt);
    }

    [Fact]
    public async Task Expired_Intent_Without_An_Object_Is_Cleared_As_Never_Uploaded()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var intent = CreateOrphan(companyId, "companies/c14/upload.xlsx", Now.AddHours(-2));

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(intent);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
        Assert.NotNull(saved.ClearedAt);
        Assert.Null(saved.DeletionEligibleAt);
        Assert.Null(saved.DeletedAt);
    }

    [Fact]
    public async Task Upload_Paused_After_Reservation_While_Reconciliation_Runs_Leaves_The_Intent_Untouched()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var intent = CreateOrphan(companyId, "companies/c15/upload.xlsx", Now);
        storage.SeedContent(intent.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(intent);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == intent.Id);
        Assert.Null(saved.DeletedAt);
        Assert.Null(saved.ClearedAt);
        Assert.Null(saved.DeletionEligibleAt);
        Assert.Null(saved.ConfirmedAt);
    }

    [Fact]
    public async Task Confirmation_Racing_Ahead_Of_The_Deletion_Phase_Prevents_Deletion_And_Leaves_The_File_In_Place()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var intent = CreateOrphan(companyId, "companies/c16/upload.xlsx", Now.AddHours(-2));
        storage.SeedContent(intent.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(intent);
            await seed.SaveChangesAsync();
        }

        await using var jobDb = BuildContext(dbName);
        var job = BuildJob(jobDb, storage);
        await job.ExecuteAsync();

        Assert.Contains(intent.StorageKey, storage.Deletions);

        var raceIntent = CreateOrphan(companyId, "companies/c17/upload.xlsx", Now.AddHours(-2));
        storage.SeedContent(raceIntent.StorageKey, [4, 5, 6]);

        await using (var seed = BuildContext(dbName))
        {
            seed.OrphanedImportFileUploads.Add(raceIntent);
            await seed.SaveChangesAsync();
        }

        await using var raceJobDb = BuildContext(dbName);
        var tracked = await raceJobDb.OrphanedImportFileUploads.SingleAsync(o => o.Id == raceIntent.Id);
        tracked.MarkDeletionEligible(Now.AddHours(-1));
        await raceJobDb.SaveChangesAsync();

        await using (var confirmingDb = BuildContext(dbName))
        {
            var confirming = await confirmingDb.OrphanedImportFileUploads.SingleAsync(o => o.Id == raceIntent.Id);
            confirming.MarkConfirmed(Now);
            await confirmingDb.SaveChangesAsync();
        }

        tracked.BeginDeletionAttempt(Now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => raceJobDb.SaveChangesAsync());

        Assert.DoesNotContain(raceIntent.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.OrphanedImportFileUploads.SingleAsync(o => o.Id == raceIntent.Id);
        Assert.NotNull(saved.ConfirmedAt);
        Assert.Null(saved.DeletedAt);
    }
}
