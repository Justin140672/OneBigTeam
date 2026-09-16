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
        RecruitmentDbContext db, string storageKey = "company/candidate/doc/cv.pdf")
    {
        var operation = CandidateDocumentDeletionOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), storageKey, Now);
        db.CandidateDocumentDeletionOperations.Add(operation);
        db.SaveChanges();
        return operation;
    }

    [Fact]
    public async Task ProcessAsync_Calls_Storage_DeleteAsync_With_Operations_StorageKey_And_Marks_Completed()
    {
        await using var db = BuildContext();
        var storageKey = "company/candidate/doc/cv.pdf";
        var operation = SeedOperation(db, storageKey);
        var storage = new FakeCandidateDocumentStorageService();

        var job = new PurgeCandidateDocumentStorageJob(db, storage, new FakeClock(FixedUtcNow), NullLogger<PurgeCandidateDocumentStorageJob>.Instance);

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
        var job = new PurgeCandidateDocumentStorageJob(db, storage, new FakeClock(FixedUtcNow), NullLogger<PurgeCandidateDocumentStorageJob>.Instance);

        await job.ProcessAsync(Guid.NewGuid());

        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task ProcessAsync_Does_Not_ReProcess_An_Already_Completed_Operation()
    {
        await using var db = BuildContext();
        var operation = SeedOperation(db);
        operation.MarkProcessing(Now);
        operation.MarkCompleted(Now);
        await db.SaveChangesAsync();

        var storage = new FakeCandidateDocumentStorageService();
        var job = new PurgeCandidateDocumentStorageJob(db, storage, new FakeClock(FixedUtcNow), NullLogger<PurgeCandidateDocumentStorageJob>.Instance);

        await job.ProcessAsync(operation.Id);

        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task ProcessAsync_Rethrows_On_NonFinal_Attempt_Without_Marking_Failed()
    {
        await using var db = BuildContext();
        var operation = SeedOperation(db);
        var storage = new ThrowingCandidateDocumentStorageService();
        var job = new PurgeCandidateDocumentStorageJob(db, storage, new FakeClock(FixedUtcNow), NullLogger<PurgeCandidateDocumentStorageJob>.Instance);

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
        // Drive AttemptCount up to MaxAttempts - 1 so the next MarkProcessing (inside ProcessAsync)
        // reaches MaxAttempts, making this the final attempt.
        for (var i = 0; i < PurgeCandidateDocumentStorageJob.MaxAttempts - 1; i++)
            operation.MarkProcessing(Now);
        await db.SaveChangesAsync();

        var storage = new ThrowingCandidateDocumentStorageService();
        var job = new PurgeCandidateDocumentStorageJob(db, storage, new FakeClock(FixedUtcNow), NullLogger<PurgeCandidateDocumentStorageJob>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.ProcessAsync(operation.Id));

        var saved = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusFailed, saved.Status);
        Assert.Equal(PurgeCandidateDocumentStorageJob.MaxAttempts, saved.AttemptCount);
        Assert.Equal("Simulated storage outage.", saved.FailureReason);
        Assert.Equal(operation.StorageKey, saved.StorageKey);
        Assert.Equal(operation.CandidateId, saved.CandidateId);
        Assert.Equal(operation.CompanyId, saved.CompanyId);
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
