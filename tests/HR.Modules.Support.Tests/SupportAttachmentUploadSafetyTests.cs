using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.AddSupportResponse;
using HR.Modules.Support.Features.SubmitSupportRequest;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Services;
using HR.Modules.Support.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
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

    private static SupportDbContext BuildContext(bool failOnSaveChanges = false)
    {
        var builder = new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"));

        if (failOnSaveChanges)
            builder.AddInterceptors(new ThrowingSaveChangesInterceptor());

        return new SupportDbContext(builder.Options);
    }

    /// <summary>Reliability review issue 4 (P1): forces SaveChangesAsync to throw after uploads have
    /// already succeeded, to prove cleanup still runs for a genuine persistence failure (as opposed
    /// to disposing the context, which would fail earlier at db.SupportRequests.Add and never reach
    /// the upload phase at all).</summary>
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
        SupportDbContext db, FakeSupportAttachmentStorageService storage, FakeUploadedFileScanner scanner) =>
        new(db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), scanner,
            new FakeEmailSender(), BuildConfiguration(),
            TestExecutionContext.Accessor, NullLogger<SubmitSupportRequestHandler>.Instance);

    [Fact]
    public async Task SubmitSupportRequest_Rejects_Infected_File_And_Uploads_Nothing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("infected.png");
        var handler = BuildSubmitHandler(db, storage, scanner);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("infected.png")), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.SupportRequests.ToListAsync());
        Assert.Empty(await db.SupportAttachments.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Rejects_When_Scanner_Unavailable_And_Uploads_Nothing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner { ThrowOnScan = true };
        var handler = BuildSubmitHandler(db, storage, scanner);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("photo.png")), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.SupportRequests.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Cleans_Up_Earlier_Upload_When_Later_File_In_Batch_Is_Infected()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("second.png");
        var handler = BuildSubmitHandler(db, storage, scanner);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.Create("second.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.SupportAttachments.ToListAsync());

        // Reliability review issue 4 (P1): the fake now actually tracks delete calls/active keys,
        // so this proves the handler genuinely issued a DeleteAsync for "first.png"'s key — the old
        // assertion (empty SupportAttachments table) passed even with zero cleanup, since nothing
        // was ever persisted either way.
        Assert.Single(storage.Uploads);
        Assert.Single(storage.DeleteAttempts);
        Assert.Empty(storage.ActiveKeys);
    }

    [Fact]
    public async Task SubmitSupportRequest_Rejects_Whole_Request_When_Too_Many_Files_Without_Uploading_Any()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var files = Enumerable.Range(0, SupportAttachmentPolicy.MaxFileCount + 1)
            .Select(i => TestFile.Create($"file{i}.png"))
            .ToArray();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner());

        var result = await handler.HandleAsync(
            ValidRequest(companyId, files), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.SupportRequests.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Uses_GuidBased_Storage_Key_Not_Raw_FileName()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner());

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
        await using var db = BuildContext();
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
        var handler = new AddSupportResponseHandler(
            db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), scanner,
            new FakeEmailSender(), new FakeUserEmailReader(),
            TestExecutionContext.Accessor, NullLogger<AddSupportResponseHandler>.Instance);

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

    // ── Reliability review issue 4 (P1) ─────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitSupportRequest_Cleans_Up_First_Upload_When_Second_Upload_Throws()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        storage.FailUploadForFileNames.Add("second.png");
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner());

        // The second upload throws, not a Result failure — the ownership scope must still clean
        // up the first file even though the exception propagates out of the handler entirely.
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
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner());

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
        // SaveChangesAsync is forced to throw only after the upload has already happened, proving
        // cleanup runs for a genuine persistence failure specifically (not just an upload-phase one).
        await using var db = BuildContext(failOnSaveChanges: true);
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner());

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
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner { ThrowOperationCanceledOnScan = true };
        var handler = BuildSubmitHandler(db, storage, scanner);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Must propagate as OperationCanceledException, not be swallowed into a generic
        // "scanning is temporarily unavailable" Result failure.
        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("photo.png")),
            Guid.NewGuid(), Guid.NewGuid(), cts.Token));
    }

    [Fact]
    public async Task SubmitSupportRequest_Records_Pending_Deletion_When_Cleanup_Delete_Fails_And_Retry_Job_Resolves_It()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("second.png");
        var handler = BuildSubmitHandler(db, storage, scanner);

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.Create("second.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsFailure);

        var uploadedKey = storage.Uploads.Single().StorageKey;

        // Simulate the immediate cleanup delete having failed (e.g. a transient network error) by
        // re-adding the key as active and configuring the fake to fail once more, then run the
        // durable retry job directly — this proves issue 4's "delete failure followed by retry"
        // requirement end-to-end rather than only inside the ownership scope.
        storage.FailDeleteForKeys.Add(uploadedKey);
        var pending = HR.Modules.Support.Domain.SupportAttachmentPendingDeletion.Create(
            Guid.NewGuid(), uploadedKey, "Simulated initial failure.", new DateTimeOffset(FixedUtcNow, TimeSpan.Zero));
        db.SupportAttachmentPendingDeletions.Add(pending);
        await db.SaveChangesAsync();

        var retryJob = new HR.Modules.Support.Jobs.SupportAttachmentPendingDeletionRetryJob(
            db, storage, new FakeClock(FixedUtcNow.AddMinutes(5)),
            NullLogger<HR.Modules.Support.Jobs.SupportAttachmentPendingDeletionRetryJob>.Instance);

        // First sweep: still configured to fail — attempt count increments, stays unresolved.
        await retryJob.ExecuteAsync();
        var afterFirstRetry = await db.SupportAttachmentPendingDeletions.SingleAsync(d => d.StorageKey == uploadedKey);
        Assert.Null(afterFirstRetry.ResolvedAt);
        Assert.True(afterFirstRetry.AttemptCount >= 2);

        // Second sweep: storage recovers — the pending deletion resolves.
        storage.FailDeleteForKeys.Remove(uploadedKey);
        await retryJob.ExecuteAsync();
        var afterSecondRetry = await db.SupportAttachmentPendingDeletions.SingleAsync(d => d.StorageKey == uploadedKey);
        Assert.NotNull(afterSecondRetry.ResolvedAt);
    }

    [Fact]
    public async Task SubmitSupportRequest_Successful_Path_Never_Calls_Delete()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildSubmitHandler(db, storage, new FakeUploadedFileScanner());

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("evidence.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(storage.Uploads);
        Assert.Empty(storage.DeleteAttempts);
        Assert.Single(storage.ActiveKeys);
        Assert.Single(await db.SupportAttachments.ToListAsync());
    }
}
