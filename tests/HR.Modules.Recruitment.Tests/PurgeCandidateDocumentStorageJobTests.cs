using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Ticket 7 (P2) / Ticket 13 (P2): PurgeCandidateDocumentStorageJob is the Hangfire-retried job that
/// deletes a purged candidate document's blob from storage, driven by the durable
/// CandidateDocumentDeletionOperation row created in the same transaction as the CandidateDocument
/// deletion (see Features/PurgeEligibleCandidates/Handler.cs). See PurgeEligibleCandidatesHandlerTests
/// for the coverage of the enqueueing/persistence behaviour that creates these operations.
/// </summary>
public class PurgeCandidateDocumentStorageJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static CandidateDocumentDeletionOperation SeedOperation(
        RecruitmentDbContext db, string storageKey = "company/candidate/doc/cv.pdf", Guid? companyId = null)
    {
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), companyId ?? Guid.NewGuid(), Guid.NewGuid(), storageKey, Now);
        db.CandidateDocumentDeletionOperations.Add(operation);
        db.SaveChanges();
        return operation;
    }

    private static PurgeCandidateDocumentStorageJob BuildJob(
        RecruitmentDbContext db,
        ICandidateDocumentStorageService storage,
        FakeLegalHoldStatusReader? legalHoldStatusReader = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db, storage, legalHoldStatusReader ?? new FakeLegalHoldStatusReader(), auditPublisher ?? new FakeAuditPublisher(),
            new FakeClock(FixedUtcNow), NullLogger<PurgeCandidateDocumentStorageJob>.Instance);

    [Fact]
    public async Task ProcessAsync_Calls_Storage_DeleteAsync_With_Operations_StorageKey_And_Marks_Completed()
    {
        await using var db = BuildContext();
        var storageKey = "company/candidate/doc/cv.pdf";
        var operation = SeedOperation(db, storageKey);
        var storage = new FakeCandidateDocumentStorageService();

        var job = BuildJob(db, storage);

        await job.ProcessAsync(operation.Id);

        var deleted = Assert.Single(storage.Deletions);
        Assert.Equal(storageKey, deleted);

        var saved = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusCompleted, saved.Status);
        Assert.NotNull(saved.CompletedAt);
        Assert.Null(saved.FailureReason);
    }

    [Fact]
    public async Task ProcessAsync_Is_A_Safe_NoOp_When_Operation_Not_Found()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var job = BuildJob(db, storage);

        await job.ProcessAsync(Guid.NewGuid());

        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task ProcessAsync_Does_Not_ReProcess_An_Already_Completed_Operation()
    {
        await using var db = BuildContext();
        var operation = SeedOperation(db);
        operation.Claim(Guid.NewGuid(), Now);
        operation.MarkCompleted(Now);
        await db.SaveChangesAsync();

        var storage = new FakeCandidateDocumentStorageService();
        var job = BuildJob(db, storage);

        await job.ProcessAsync(operation.Id);

        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task ProcessAsync_Rethrows_On_NonFinal_Attempt_Without_Marking_Failed()
    {
        await using var db = BuildContext();
        var operation = SeedOperation(db);
        var storage = new ThrowingCandidateDocumentStorageService();
        var job = BuildJob(db, storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(operation.Id));

        var saved = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusProcessing, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Null(saved.FailureReason);
    }

    [Fact]
    public async Task ProcessAsync_Marks_Failed_On_Final_Attempt_Retaining_Context()
    {
        await using var db = BuildContext();
        var operation = SeedOperation(db);
        // Drive AttemptCount up to MaxAttempts - 1 so the next Claim (inside ProcessAsync)
        // reaches MaxAttempts, making this the final attempt.
        for (var i = 0; i < PurgeCandidateDocumentStorageJob.MaxAttempts - 1; i++)
            operation.Claim(Guid.NewGuid(), Now);
        await db.SaveChangesAsync();

        var storage = new ThrowingCandidateDocumentStorageService();
        var job = BuildJob(db, storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(operation.Id));

        var saved = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusFailed, saved.Status);
        Assert.Equal(PurgeCandidateDocumentStorageJob.MaxAttempts, saved.AttemptCount);
        Assert.Equal("Simulated storage outage.", saved.FailureReason);
        Assert.Equal(operation.StorageKey, saved.StorageKey);
        Assert.Equal(operation.CandidateId, saved.CandidateId);
        Assert.Equal(operation.CompanyId, saved.CompanyId);
    }

    // ---- Ticket 18 (P1): legal-hold recheck in the actual deletion worker ----------------------

    [Fact]
    public async Task ProcessAsync_Suspends_Without_Deleting_Or_Consuming_An_Attempt_When_Company_Is_Under_Legal_Hold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var operation = SeedOperation(db, companyId: companyId);
        var storage = new FakeCandidateDocumentStorageService();
        var legalHold = new FakeLegalHoldStatusReader(companyId);
        var auditPublisher = new FakeAuditPublisher();

        var job = BuildJob(db, storage, legalHold, auditPublisher);

        await job.ProcessAsync(operation.Id);

        Assert.Empty(storage.Deletions);

        var saved = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusHeld, saved.Status);
        Assert.Equal(0, saved.AttemptCount); // A hold must never consume a retry attempt.
        Assert.Null(saved.FailureReason);
        Assert.Equal(operation.StorageKey, saved.StorageKey); // Storage key retained for later resume.

        Assert.Single(auditPublisher.Published.OfType<CandidateDocumentDeletionSuspendedForLegalHoldAuditEvent>());
    }

    [Fact]
    public async Task ProcessAsync_Suspends_A_Hold_Placed_Between_Failed_Retries_Without_Duplicating_The_Suspend_Audit()
    {
        // A hold placed AFTER an earlier failed attempt (not just before the very first one) — the
        // operation is already sitting Pending (reset by the reconciliation sweep) with a non-zero
        // AttemptCount from its prior failure when the hold takes effect.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var operation = SeedOperation(db, companyId: companyId);
        operation.Claim(Guid.NewGuid(), Now);
        operation.MarkFailed("Simulated storage outage.", Now, PurgeCandidateDocumentStorageJob.MaxAttempts);
        operation.ResetForRetry();
        await db.SaveChangesAsync();

        var storage = new FakeCandidateDocumentStorageService();
        var legalHold = new FakeLegalHoldStatusReader(companyId);
        var auditPublisher = new FakeAuditPublisher();
        var job = BuildJob(db, storage, legalHold, auditPublisher);

        await job.ProcessAsync(operation.Id);
        var attemptCountAfterFirstHeldCheck = (await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id)).AttemptCount;

        // A second sweep/worker attempt while STILL held must not consume another attempt or emit a
        // second suspend notice.
        await job.ProcessAsync(operation.Id);

        var saved = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusHeld, saved.Status);
        Assert.Equal(1, attemptCountAfterFirstHeldCheck); // Only the earlier genuine failure counted.
        Assert.Equal(1, saved.AttemptCount);
        Assert.Empty(storage.Deletions);

        Assert.Single(auditPublisher.Published.OfType<CandidateDocumentDeletionSuspendedForLegalHoldAuditEvent>());
    }

    [Fact]
    public async Task ProcessAsync_Deletes_Normally_When_Company_Is_Not_Under_Legal_Hold()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var operation = SeedOperation(db, companyId: companyId);
        var storage = new FakeCandidateDocumentStorageService();
        var legalHold = new FakeLegalHoldStatusReader(); // No held companies.
        var auditPublisher = new FakeAuditPublisher();

        await BuildJob(db, storage, legalHold, auditPublisher).ProcessAsync(operation.Id);

        var saved = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusCompleted, saved.Status);
        Assert.Single(storage.Deletions);
        Assert.Empty(auditPublisher.Published.OfType<CandidateDocumentDeletionSuspendedForLegalHoldAuditEvent>());
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class ThrowingCandidateDocumentStorageService : ICandidateDocumentStorageService
    {
        public Task<string> UploadAsync(Stream content, string fileName, string contentType, string storageFolder, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Uri> GetDownloadUrlAsync(string storageKey, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Simulated storage outage.");
    }
}
