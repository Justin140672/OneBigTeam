using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.AddSupportResponse;
using HR.Modules.Support.Features.SubmitSupportRequest;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Services;
using HR.Modules.Support.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Support.Tests;

/// <summary>
/// Security review ticket 4 (P1) / Reliability review issue 4 (P1): end-to-end handler behaviour
/// for the reject-before-upload, scan-before-persist, and guaranteed cleanup-on-any-failure
/// requirements — beyond what SupportAttachmentValidatorTests covers in isolation.
/// </summary>
public class SupportAttachmentUploadSafetyTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset SeedNow = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static SupportDbContext BuildContext(string dbName, bool failOnSaveChanges = false)
    {
        var builder = new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(dbName);

        if (failOnSaveChanges)
            builder.AddInterceptors(new ThrowingSaveChangesInterceptor());

        return new SupportDbContext(builder.Options);
    }

    private static IServiceScopeFactory BuildScopeFactory(string dbName) =>
        new ServiceCollection()
            .AddDbContext<SupportDbContext>(o => o.UseInMemoryDatabase(dbName))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    /// <summary>
    /// Security review finding #4 (P1): the in-memory EF provider doesn't enforce unique indexes,
    /// so the real "storage_key where resolved_at is null" unique constraint can't be triggered by
    /// data alone. This interceptor simulates the constraint by throwing <see cref="DbUpdateException"/>
    /// whenever a newly-added <see cref="SupportAttachmentPendingDeletion"/> is saved for a storage
    /// key that already has an unresolved row — proving
    /// <see cref="UploadedAttachmentCleanupScope"/>'s catch/duplicate-check/no-op code path actually
    /// runs and behaves correctly, exactly as it would against a real unique constraint violation.
    /// </summary>
    private static IServiceScopeFactory BuildDuplicateKeyThrowingScopeFactory(string dbName) =>
        new ServiceCollection()
            .AddDbContext<SupportDbContext>(o => o
                .UseInMemoryDatabase(dbName)
                .AddInterceptors(new ThrowOnDuplicateUnresolvedStorageKeyInterceptor()))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private sealed class ThrowOnDuplicateUnresolvedStorageKeyInterceptor : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is SupportDbContext context)
            {
                var addedKeys = context.ChangeTracker
                    .Entries<SupportAttachmentPendingDeletion>()
                    .Where(e => e.State == EntityState.Added)
                    .Select(e => e.Entity.StorageKey)
                    .ToList();

                foreach (var key in addedKeys)
                {
                    var exists = await context.SupportAttachmentPendingDeletions
                        .AsNoTracking()
                        .AnyAsync(d => d.StorageKey == key && d.ResolvedAt == null, cancellationToken);

                    if (exists)
                        throw new DbUpdateException("Simulated unique constraint violation on storage_key.");
                }
            }

            return result;
        }
    }

    private sealed class ThrowingSaveChangesInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated persistence failure.");
    }

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Support:AdminNotificationEmail"] = null })
            .Build();

    private static SubmitSupportRequestRequest ValidRequest(Guid companyId, params Microsoft.AspNetCore.Http.IFormFile[] files) => new()
    {
        CompanyId = companyId,
        Type = SupportRequestType.ReportProblem,
        Title = "Title",
        Description = "Description",
        Priority = SupportRequestPriority.Medium,
        Files = TestFile.Collection(files),
    };

    private static SubmitSupportRequestHandler BuildSubmitHandler(
        SupportDbContext db, FakeSupportAttachmentStorageService storage, FakeUploadedFileScanner scanner,
        string dbName) =>
        new(db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), scanner,
            new FakeEmailSender(), BuildConfiguration(),
            TestExecutionContext.Accessor, BuildScopeFactory(dbName),
            NullLogger<SubmitSupportRequestHandler>.Instance);

    private static AddSupportResponseHandler BuildAddResponseHandler(
        SupportDbContext db, FakeSupportAttachmentStorageService storage, FakeUploadedFileScanner scanner,
        string dbName) =>
        new(db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), scanner,
            new FakeEmailSender(), new FakeUserEmailReader(),
            TestExecutionContext.Accessor, BuildScopeFactory(dbName),
            NullLogger<AddSupportResponseHandler>.Instance);

    [Fact]
    public async Task SubmitSupportRequest_Rejects_Infected_File_And_Uploads_Nothing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("infected.png");
        var handler = BuildSubmitHandler(db, storage, scanner, dbName);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("infected.png")), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.SupportRequests.ToListAsync());
        Assert.Empty(await db.SupportAttachments.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Rejects_When_Scanner_Unavailable_And_Uploads_Nothing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner { ThrowOnScan = true };
        var handler = BuildSubmitHandler(db, storage, scanner, dbName);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("photo.png")), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.SupportRequests.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Cleans_Up_Earlier_Upload_When_Later_File_In_Batch_Is_Infected()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("second.png");
        var handler = BuildSubmitHandler(db, storage, scanner, dbName);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.Create("second.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.SupportAttachments.ToListAsync());

        Assert.Single(storage.Uploads);
        Assert.Single(storage.DeleteAttempts);
        Assert.Empty(storage.ActiveKeys);
    }

    [Fact]
    public async Task SubmitSupportRequest_Rejects_Whole_Request_When_Too_Many_Files_Without_Uploading_Any()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var files = Enumerable.Range(0, SupportAttachmentPolicy.MaxFileCount + 1)
            .Select(i => TestFile.Create($"file{i}.png"))
            .ToArray();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner(), dbName);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, files), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.SupportRequests.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Uses_GuidBased_Storage_Key_Not_Raw_FileName()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner(), dbName);

        await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("../../malicious name.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var attachment = await db.SupportAttachments.SingleAsync();
        Assert.DoesNotContain("..", attachment.FileName);
        Assert.DoesNotContain("/", attachment.FileName);
    }

    [Fact]
    public async Task AddSupportResponse_Rejects_Infected_File_And_Persists_No_Response_Attachment()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = SupportRequest.Create(
            Guid.NewGuid(), companyId, submitter, null,
            SupportRequestType.AskQuestion, "Title", "Description", SupportRequestPriority.Low,
            "SUP-1", null, null, null, false, null, null, SeedNow);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("infected.png");
        var handler = BuildAddResponseHandler(db, storage, scanner, dbName);

        var result = await handler.HandleAsync(
            new AddSupportResponseRequest
            {
                CompanyId = companyId,
                Id = request.Id,
                BodyHtml = "See attached",
                Files = TestFile.Collection(TestFile.Create("infected.png")),
            },
            submitter, isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.SupportResponses.ToListAsync());
        Assert.Empty(await db.SupportResponseAttachments.ToListAsync());
    }


    [Fact]
    public async Task SubmitSupportRequest_Cleans_Up_First_Upload_When_Second_Upload_Throws()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        storage.FailUploadForFileNames.Add("second.png");
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner(), dbName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.Create("second.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.DeleteAttempts);
        Assert.Empty(storage.ActiveKeys);
        Assert.Empty(await db.SupportAttachments.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Cleans_Up_First_Upload_When_Stream_Copy_Throws()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner(), dbName);

        await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.CreateWithThrowingStream("broken.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.DeleteAttempts);
        Assert.Empty(storage.ActiveKeys);
        Assert.Empty(await db.SupportAttachments.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Cleans_Up_Uploads_When_SaveChanges_Fails()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName, failOnSaveChanges: true);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner(), dbName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("evidence.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.DeleteAttempts);
        Assert.Empty(storage.ActiveKeys);
    }

    [Fact]
    public async Task SubmitSupportRequest_Propagates_OperationCanceledException_On_Caller_Cancellation()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner { ThrowOperationCanceledOnScan = true };
        var handler = BuildSubmitHandler(db, storage, scanner, dbName);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("photo.png")),
            Guid.NewGuid(), Guid.NewGuid(), cts.Token));

        // No file was ever uploaded (cancellation happens before any upload call), so the cleanup
        // scope has nothing tracked and must not create a pending-deletion record.
        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.SupportAttachmentPendingDeletions.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Records_Pending_Deletion_When_Cleanup_Delete_Fails_And_Retry_Job_Resolves_It()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("second.png");
        var handler = BuildSubmitHandler(db, storage, scanner, dbName);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.Create("second.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsFailure);

        var uploadedKey = storage.Uploads.Single().StorageKey;

        storage.FailDeleteForKeys.Add(uploadedKey);
        var pending = HR.Modules.Support.Domain.SupportAttachmentPendingDeletion.Create(
            Guid.NewGuid(), uploadedKey, "Simulated initial failure.", new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        db.SupportAttachmentPendingDeletions.Add(pending);
        await db.SaveChangesAsync();

        var retryJob = new HR.Modules.Support.Jobs.SupportAttachmentPendingDeletionRetryJob(
            db, storage, new FakeClock(FixedUtcNow.AddMinutes(5)),
            NullLogger<HR.Modules.Support.Jobs.SupportAttachmentPendingDeletionRetryJob>.Instance);

        await retryJob.ExecuteAsync();
        var afterFirstRetry = await db.SupportAttachmentPendingDeletions.SingleAsync(d => d.StorageKey == uploadedKey);
        Assert.Null(afterFirstRetry.ResolvedAt);
        Assert.True(afterFirstRetry.AttemptCount >= 2);

        storage.FailDeleteForKeys.Remove(uploadedKey);
        await retryJob.ExecuteAsync();
        var afterSecondRetry = await db.SupportAttachmentPendingDeletions.SingleAsync(d => d.StorageKey == uploadedKey);
        Assert.NotNull(afterSecondRetry.ResolvedAt);
    }

    [Fact]
    public async Task SubmitSupportRequest_Successful_Path_Never_Calls_Delete()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner(), dbName);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("evidence.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(storage.Uploads);
        Assert.Empty(storage.DeleteAttempts);
        Assert.Single(storage.ActiveKeys);
        Assert.Single(await db.SupportAttachments.ToListAsync());
    }


    [Fact]
    public async Task SubmitSupportRequest_Isolates_PendingDeletion_From_Failed_Request_Graph_When_Compensating_Delete_Also_Fails()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService { FailAllDeletes = true };
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("second.png");
        var handler = BuildSubmitHandler(db, storage, scanner, dbName);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.Create("second.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);

        var uploadedKey = storage.Uploads.Single().StorageKey;
        var pendingDeletions = await db.SupportAttachmentPendingDeletions.ToListAsync();
        Assert.Single(pendingDeletions);
        Assert.Equal(uploadedKey, pendingDeletions[0].StorageKey);
        Assert.Null(pendingDeletions[0].ResolvedAt);

        Assert.Empty(await db.SupportRequests.ToListAsync());
        Assert.Empty(await db.SupportAttachments.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Records_PendingDeletion_When_Upload_Throws_And_Compensating_Delete_Also_Fails()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService { FailAllDeletes = true };
        storage.FailUploadForFileNames.Add("second.png");
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner(), dbName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.Create("second.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        var uploadedKey = storage.Uploads.Single().StorageKey;
        var pendingDeletions = await db.SupportAttachmentPendingDeletions.ToListAsync();
        Assert.Single(pendingDeletions);
        Assert.Equal(uploadedKey, pendingDeletions[0].StorageKey);

        Assert.Empty(await db.SupportRequests.ToListAsync());
        Assert.Empty(await db.SupportAttachments.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Records_PendingDeletion_Via_Separate_Context_Even_When_Ambient_Context_Always_Fails()
    {
        var dbName = Guid.NewGuid().ToString("N");
        await using var db = BuildContext(dbName, failOnSaveChanges: true);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService { FailAllDeletes = true };
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner(), dbName);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("evidence.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        var uploadedKey = storage.Uploads.Single().StorageKey;

        await using var verifyDb = BuildContext(dbName);
        var pendingDeletions = await verifyDb.SupportAttachmentPendingDeletions.ToListAsync();
        Assert.Single(pendingDeletions);
        Assert.Equal(uploadedKey, pendingDeletions[0].StorageKey);
        Assert.Empty(await verifyDb.SupportRequests.ToListAsync());
    }

    /// <summary>
    /// Security review finding #4 (P1): exercises the idempotency guard directly at the
    /// <see cref="UploadedAttachmentCleanupScope"/> level. An unresolved pending-deletion row
    /// already exists for a storage key; the scope's own insert attempt for the same key is made
    /// to fail with <see cref="DbUpdateException"/> (simulating the real unique index on
    /// <c>storage_key where resolved_at is null</c> — the in-memory provider doesn't enforce
    /// unique constraints, so this is simulated via an interceptor). The scope must treat this as
    /// a no-op — no exception propagates, and exactly one unresolved row remains for the key.
    /// </summary>
    [Fact]
    public async Task CleanupScope_Treats_Duplicate_Unresolved_PendingDeletion_As_NoOp()
    {
        var dbName = Guid.NewGuid().ToString("N");
        const string storageKey = "support/company-id/request-id/duplicate.png";

        await using (var seedDb = BuildContext(dbName))
        {
            seedDb.SupportAttachmentPendingDeletions.Add(
                SupportAttachmentPendingDeletion.Create(
                    Guid.NewGuid(), storageKey, "Pre-existing failure.",
                    new DateTimeOffset(FixedUtcNow, TimeSpan.Zero)));
            await seedDb.SaveChangesAsync();
        }

        var scopeFactory = BuildDuplicateKeyThrowingScopeFactory(dbName);
        var storage = new FakeSupportAttachmentStorageService();
        storage.FailDeleteForKeys.Add(storageKey);

        await using (var cleanupScope = new UploadedAttachmentCleanupScope(
            storage, scopeFactory, new FakeClock(FixedUtcNow), TestExecutionContext.Accessor,
            NullLogger<UploadedAttachmentCleanupScope>.Instance))
        {
            cleanupScope.Track(storageKey);
        }

        await using var assertDb = BuildContext(dbName);
        var rows = await assertDb.SupportAttachmentPendingDeletions
            .Where(d => d.StorageKey == storageKey)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Null(rows[0].ResolvedAt);
    }
}
