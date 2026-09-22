using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DeleteCandidateDocument;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

public class DeleteCandidateDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DeleteCandidateDocumentHandler BuildHandler(
        RecruitmentDbContext db,
        FakeCandidateDocumentStorageService? storage = null,
        RecordingBackgroundJobClient? backgroundJobClient = null) =>
        new(db,
            storage ?? new FakeCandidateDocumentStorageService(),
            new FakeClock(FixedUtcNow),
            backgroundJobClient ?? new RecordingBackgroundJobClient(),
            NullLogger<DeleteCandidateDocumentHandler>.Instance);

    private static async Task<(Candidate Candidate, CandidateDocument Document)> SeedCandidateWithDocument(
        RecruitmentDbContext db, Guid companyId, string storageKey = "storage/key/resume.pdf")
    {
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var document = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", storageKey, Guid.NewGuid(), Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();
        return (candidate, document);
    }

    [Fact]
    public async Task HandleAsync_Removes_Document_And_Deletes_From_Storage()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var (candidate, document) = await SeedCandidateWithDocument(db, companyId);

        var result = await BuildHandler(db, storage).HandleAsync(
            new DeleteCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.CandidateDocuments.ToListAsync());
        Assert.Single(storage.Deletions);
        Assert.Equal(document.StorageKey, storage.Deletions[0]);

        // Happy path: the operation row ends Completed, not left dangling as Pending.
        var operation = await db.CandidateDocumentDeletionOperations.SingleAsync();
        Assert.Equal(CandidateDocumentDeletionOperation.StatusCompleted, operation.Status);
        Assert.NotNull(operation.CompletedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Missing()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();

        var result = await BuildHandler(db, storage).HandleAsync(
            new DeleteCandidateDocumentRequest { CompanyId = Guid.NewGuid(), CandidateId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(storage.Deletions);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Different_Candidate()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var otherCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var document = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", "key", Guid.NewGuid(), Now);
        db.Candidates.AddRange(candidate, otherCandidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await BuildHandler(db, storage).HandleAsync(
            new DeleteCandidateDocumentRequest { CompanyId = companyId, CandidateId = otherCandidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Success_And_Enqueues_Retry_When_Inline_Blob_Delete_Fails()
    {
        // Security/code review finding #3: the durable operation row is created in the SAME
        // transaction as the document-row removal, so even when the best-effort inline delete
        // fails, the overall Result is still success — the reconciliation sweep guarantees eventual
        // cleanup, so callers must not be told the deletion failed.
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService { ThrowOnNextDeleteAttempts = 1 };
        var companyId = Guid.NewGuid();
        var (candidate, document) = await SeedCandidateWithDocument(db, companyId, "storage/key/failing.pdf");

        var jobClient = new RecordingBackgroundJobClient();

        var result = await BuildHandler(db, storage, jobClient).HandleAsync(
            new DeleteCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The document row is gone (it was removed in the same transaction as the operation row).
        Assert.Empty(await db.CandidateDocuments.ToListAsync());
        Assert.Empty(storage.Deletions);

        // The operation row remains Pending (never claimed/completed here) so the reconciliation
        // sweep can retry it — the storage key is still recoverable from it, not lost.
        var operation = await db.CandidateDocumentDeletionOperations.SingleAsync();
        Assert.Equal(CandidateDocumentDeletionOperation.StatusPending, operation.Status);
        Assert.Equal(document.StorageKey, operation.StorageKey);
        Assert.Null(operation.CompletedAt);

        Assert.Single(jobClient.CreatedJobs);
        Assert.Equal(nameof(PurgeCandidateDocumentStorageJob.ProcessAsync), jobClient.CreatedJobs[0].Method.Name);
    }

    [Fact]
    public async Task HandleAsync_Persists_The_Operation_Row_With_Document_Removal_Even_If_The_Inline_Delete_Is_Never_Attempted()
    {
        // Invariant check: the mark-for-deletion-first pattern means the storage key survives on
        // the durable operation row the moment the document row is removed — even simulating a
        // "crash" immediately after that commit (by never invoking storage at all here) still
        // leaves the storage key fully recoverable.
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var (candidate, document) = await SeedCandidateWithDocument(db, companyId, "storage/key/crash-after-commit.pdf");

        await BuildHandler(db, storage).HandleAsync(
            new DeleteCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        // The storage key was never only "in memory" between the document removal and the delete —
        // it is durably readable back from the operation row after the handler returns.
        var operation = await db.CandidateDocumentDeletionOperations.SingleAsync();
        Assert.Equal(document.StorageKey, operation.StorageKey);
        Assert.Equal(companyId, operation.CompanyId);
        Assert.Equal(candidate.Id, operation.CandidateId);
    }

    [Fact]
    public async Task Calling_HandleAsync_Twice_For_The_Same_Document_Does_Not_Error_On_The_Second_Call()
    {
        // Idempotency: a second delete attempt (e.g. a retried request) against an already-deleted
        // document must not double-delete or throw — it should simply report NotFound since the
        // row is already gone.
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var (candidate, document) = await SeedCandidateWithDocument(db, companyId);

        var request = new DeleteCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, DocumentId = document.Id };

        var first = await BuildHandler(db, storage).HandleAsync(request, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Single(storage.Deletions);

        var second = await BuildHandler(db, storage).HandleAsync(request, CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal("not_found", second.Error.Code);

        // No second (duplicate) deletion attempt was made against storage.
        Assert.Single(storage.Deletions);
    }
}
