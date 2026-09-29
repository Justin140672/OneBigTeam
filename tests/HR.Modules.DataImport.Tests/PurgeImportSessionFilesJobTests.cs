using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Jobs;
using HR.Modules.DataImport.Persistence;
using HR.Modules.DataImport.Services;
using HR.Modules.DataImport.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Tests;

public class PurgeImportSessionFilesJobTests
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
    private static readonly DateTimeOffset InsideAbandonedWindow = Now.AddDays(-3);
    private static readonly DateTimeOffset PastAbandonedWindow = Now.AddDays(-10);

    private static DataImportDbContext BuildContext(string databaseName) =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static PurgeImportSessionFilesJob BuildJob(
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
            NullLogger<PurgeImportSessionFilesJob>.Instance);

    private static ImportSession CreateSession(
        Guid companyId,
        string storageKey = "sessions/s1/employees.xlsx") =>
        ImportSession.Create(
            Guid.NewGuid(),
            companyId,
            "Employees",
            "employees.xlsx",
            totalRows: 10,
            Guid.NewGuid(),
            storageKey,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            Now.AddDays(-30));

    [Fact]
    public async Task Successful_Import_Past_Retry_Grace_Has_Its_File_Deleted()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = CreateSession(companyId, "sessions/s1/employees.xlsx");
        session.Start(Now.AddDays(-1));
        session.Confirm(createdCount: 10, failedCount: 0, PastRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Contains(session.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(saved.FileDeletedAt);
    }

    [Fact]
    public async Task CompletedWithErrors_Session_With_A_Previously_Failed_Attempt_Past_Retry_Grace_Is_Retried_And_Succeeds()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = CreateSession(companyId, "sessions/s2/employees.xlsx");
        session.Start(Now.AddDays(-1));
        session.Confirm(createdCount: 8, failedCount: 2, Now.AddDays(-1));
        session.RecordFileDeletionAttemptFailed(PastRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Contains(session.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(saved.FileDeletedAt);
        Assert.Equal(1, saved.FileDeletionAttemptCount);
    }

    [Fact]
    public async Task Abandoned_Pending_Session_Past_The_Retention_Window_Is_Purged()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = ImportSession.Create(
            Guid.NewGuid(), companyId, "Employees", "employees.xlsx", 10, Guid.NewGuid(),
            "sessions/s3/employees.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            PastAbandonedWindow);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Contains(session.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(saved.FileDeletedAt);
    }

    [Fact]
    public async Task Abandoned_Processing_Session_Inside_The_Retention_Window_Is_Not_Touched()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = ImportSession.Create(
            Guid.NewGuid(), companyId, "Employees", "employees.xlsx", 10, Guid.NewGuid(),
            "sessions/s4/employees.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            InsideAbandonedWindow);
        session.Start(InsideAbandonedWindow);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.DoesNotContain(session.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(saved.FileDeletedAt);
        Assert.Equal(0, saved.FileDeletionAttemptCount);
    }

    [Fact]
    public async Task Cancelled_Session_Past_Retry_Grace_Has_Its_File_Deleted()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = CreateSession(companyId, "sessions/s5/employees.xlsx");
        session.Cancel(PastRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Contains(session.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.NotNull(saved.FileDeletedAt);
    }

    [Fact]
    public async Task Cancelled_Session_Inside_Retry_Grace_Is_Not_Touched()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = CreateSession(companyId, "sessions/s6/employees.xlsx");
        session.Cancel(InsideRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(saved.FileDeletedAt);
    }

    [Fact]
    public async Task Running_ExecuteAsync_Twice_Against_The_Same_Eligible_Session_Only_Deletes_Once()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = CreateSession(companyId, "sessions/s7/employees.xlsx");
        session.Cancel(PastRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
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
        Assert.Equal(session.StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task Transient_Deletion_Failure_Records_A_Failed_Attempt_And_Does_Not_Throw()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var session = CreateSession(companyId, "sessions/s8/employees.xlsx");
        session.Cancel(PastRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.DoesNotContain(session.StorageKey, storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(saved.FileDeletedAt);
        Assert.Equal(1, saved.FileDeletionAttemptCount);
        Assert.NotNull(saved.FileDeletionLastAttemptedAt);
    }

    [Fact]
    public async Task Deletion_Attempt_Count_Reaching_The_Exhausted_Threshold_Raises_An_Administrative_Alert()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var alerts = new FakeAdministrativeAlertWriter();
        var session = CreateSession(companyId, "sessions/s9/employees.xlsx");
        session.Cancel(Now.AddDays(-1));
        session.RecordFileDeletionAttemptFailed(PastRetryGrace);
        session.RecordFileDeletionAttemptFailed(PastRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage, alerts: alerts).ExecuteAsync();
        }

        var raised = Assert.Single(alerts.Raised);
        Assert.Equal(companyId, raised.CompanyId);
        Assert.Equal(HR.Infrastructure.Abstractions.AdministrativeAlertCategory.Compliance, raised.Category);
        Assert.Equal(session.Id, raised.AffectedEntityId);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Equal(3, saved.FileDeletionAttemptCount);
        Assert.Null(saved.FileDeletedAt);
    }

    [Fact]
    public async Task Attempt_Count_Below_The_Exhausted_Threshold_Does_Not_Raise_An_Administrative_Alert()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService { ThrowOnNextDeleteAttempts = 1 };
        var alerts = new FakeAdministrativeAlertWriter();
        var session = CreateSession(companyId, "sessions/s10/employees.xlsx");
        session.Cancel(PastRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage, alerts: alerts).ExecuteAsync();
        }

        Assert.Empty(alerts.Raised);
    }

    [Fact]
    public async Task Session_Under_Legal_Hold_Is_Skipped_And_Never_Attempted()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var legalHold = new FakeLegalHoldStatusReader(companyId);
        var session = CreateSession(companyId, "sessions/s11/employees.xlsx");
        session.Cancel(PastRetryGrace);
        storage.SeedContent(session.StorageKey, [1, 2, 3]);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage, legalHold: legalHold).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);

        await using var verify = BuildContext(dbName);
        var saved = await verify.ImportSessions.SingleAsync(s => s.Id == session.Id);
        Assert.Null(saved.FileDeletedAt);
        Assert.Equal(0, saved.FileDeletionAttemptCount);
    }

    [Fact]
    public async Task Session_Whose_File_Is_Already_Deleted_Is_Not_A_Candidate()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = CreateSession(companyId, "sessions/s12/employees.xlsx");
        session.Cancel(PastRetryGrace);
        session.MarkFileDeleted(Now.AddDays(-1));

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task Session_With_A_Blank_StorageKey_Is_Not_A_Candidate()
    {
        var companyId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString("N");
        var storage = new FakeImportFileStorageService();
        var session = CreateSession(companyId, storageKey: "");
        session.Cancel(PastRetryGrace);

        await using (var seed = BuildContext(dbName))
        {
            seed.ImportSessions.Add(session);
            await seed.SaveChangesAsync();
        }

        await using (var db = BuildContext(dbName))
        {
            await BuildJob(db, storage).ExecuteAsync();
        }

        Assert.Empty(storage.Deletions);
    }
}
