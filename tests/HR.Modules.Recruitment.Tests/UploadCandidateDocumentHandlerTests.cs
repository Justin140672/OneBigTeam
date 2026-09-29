using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UploadCandidateDocument;
using HR.Modules.Recruitment.Jobs;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Tests;

public class UploadCandidateDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static UploadCandidateDocumentHandler BuildHandler(
        RecruitmentDbContext db,
        FakeCandidateDocumentStorageService? storage = null,
        CandidateDocumentUploadOptions? options = null) =>
        new(db,
            storage ?? new FakeCandidateDocumentStorageService(),
            Options.Create(options ?? new CandidateDocumentUploadOptions()),
            new FakeClock(FixedUtcNow),
            NullLogger<UploadCandidateDocumentHandler>.Instance);

    private static IFormFile FakePdfFile(string fileName = "resume.pdf", int size = 1024) =>
        FakeFile(fileName, "application/pdf", new byte[size]);

    private static IFormFile FakeFile(string fileName, string contentType, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    private static async Task<Candidate> SeedCandidate(RecruitmentDbContext db, Guid companyId, Guid? id = null)
    {
        var candidate = Candidate.Create(id ?? Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();
        return candidate;
    }

    [Fact]
    public async Task HandleAsync_Creates_CandidateDocument()
    {
        await using var db = BuildContext();
        var storage        = new FakeCandidateDocumentStorageService();
        var companyId      = Guid.NewGuid();
        var uploadedBy     = Guid.NewGuid();
        var candidate      = await SeedCandidate(db, companyId);
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile() },
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId,   result.Value!.CompanyId);
        Assert.Equal(candidate.Id, result.Value.CandidateId);
        Assert.Equal("Resume",    result.Value.Title);
        Assert.Equal("resume.pdf", result.Value.FileName);

        var saved = await db.CandidateDocuments.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
        Assert.Equal(uploadedBy, saved.UploadedBy);
        Assert.Single(storage.Uploads);
    }

    [Fact]
    public async Task HandleAsync_Persists_Cv_Kind_When_Kind_Is_Cv()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = await SeedCandidate(db, companyId);
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", Kind = "Cv", File = FakePdfFile() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cv", result.Value!.Kind);

        var saved = await db.CandidateDocuments.SingleAsync();
        Assert.Equal(CandidateDocumentKind.Cv, saved.Kind);
    }

    [Fact]
    public async Task HandleAsync_Defaults_To_Other_Kind_When_Kind_Omitted()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = await SeedCandidate(db, companyId);
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Other", result.Value!.Kind);

        var saved = await db.CandidateDocuments.SingleAsync();
        Assert.Equal(CandidateDocumentKind.Other, saved.Kind);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Missing()
    {
        await using var db = BuildContext();
        var handler         = BuildHandler(db);

        var result = await handler.HandleAsync(
            new UploadCandidateDocumentRequest { CompanyId = Guid.NewGuid(), CandidateId = Guid.NewGuid(), Title = "Resume", File = FakePdfFile() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Too_Large()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var candidate       = await SeedCandidate(db, companyId);
        var handler         = BuildHandler(db, options: new CandidateDocumentUploadOptions { MaxFileSizeBytes = 100 });

        var result = await handler.HandleAsync(
            new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile(size: 200) },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Extension_Not_Allowed()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var candidate       = await SeedCandidate(db, companyId);
        var handler         = BuildHandler(db);

        var result = await handler.HandleAsync(
            new UploadCandidateDocumentRequest
            {
                CompanyId   = companyId,
                CandidateId = candidate.Id,
                Title       = "Resume",
                File        = FakeFile("malware.exe", "application/octet-stream", new byte[10]),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".exe", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_ContentType_Not_Allowed()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var candidate       = await SeedCandidate(db, companyId);
        var handler         = BuildHandler(db);

        var result = await handler.HandleAsync(
            new UploadCandidateDocumentRequest
            {
                CompanyId   = companyId,
                CandidateId = candidate.Id,
                Title       = "Resume",
                File        = FakeFile("resume.pdf", "text/html", new byte[10]),
            },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_StorageKey_Contains_CompanyId_And_CandidateId()
    {
        await using var db = BuildContext();
        var storage         = new FakeCandidateDocumentStorageService();
        var companyId       = Guid.NewGuid();
        var candidate       = await SeedCandidate(db, companyId);
        var handler         = BuildHandler(db, storage);

        await handler.HandleAsync(
            new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile() },
            Guid.NewGuid(),
            CancellationToken.None);

        var storageKey = storage.Uploads[0].StorageKey;
        Assert.Contains(companyId.ToString(), storageKey);
        Assert.Contains(candidate.Id.ToString(), storageKey);
    }

    [Fact]
    public async Task HandleAsync_Deletes_StorageObject_When_DbSave_Fails()
    {
        var storage    = new FakeCandidateDocumentStorageService();
        var companyId  = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ThrowOnceRecruitmentDbContext(options);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.BaseSaveChangesAsync();

        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(
                new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile() },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);

        var operation = await db.CandidateDocumentDeletionOperations.SingleAsync();
        Assert.Equal(CandidateDocumentDeletionOperation.StatusReserved, operation.Status);
        Assert.NotNull(operation.ConfirmedAt);
    }

    [Fact]
    public async Task HandleAsync_Compensates_Using_An_Independent_Cleanup_Token_Not_The_Cancelled_Request_Token()
    {
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        using var requestCts = new CancellationTokenSource();

        var options = new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new CancellingOnSaveRecruitmentDbContext(options, requestCts);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.BaseSaveChangesAsync();

        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.HandleAsync(
                new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile() },
                Guid.NewGuid(),
                requestCts.Token));

        Assert.True(requestCts.IsCancellationRequested);

        Assert.Single(storage.Deletions);
        var deleteToken = Assert.Single(storage.DeleteCancellationTokens);
        Assert.False(deleteToken.IsCancellationRequested);
        Assert.NotEqual(requestCts.Token, deleteToken);
    }

    [Fact]
    public async Task HandleAsync_DbSave_Fails_And_Immediate_Delete_Also_Fails_Leaves_The_Reserved_Intent_Unconfirmed_For_Reconciliation()
    {
        var storage = new FakeCandidateDocumentStorageService { ThrowOnNextDeleteAttempts = 1 };
        var companyId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ThrowOnceRecruitmentDbContext(options);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.BaseSaveChangesAsync();

        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(
                new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile() },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Empty(storage.Deletions);

        var operation = await db.CandidateDocumentDeletionOperations.SingleOrDefaultAsync();
        Assert.NotNull(operation);
        Assert.Equal(companyId, operation!.CompanyId);
        Assert.Equal(candidate.Id, operation.CandidateId);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusReserved, operation.Status);
        Assert.Null(operation.ConfirmedAt);

        var reconciliationStorage = new FakeCandidateDocumentStorageService();
        reconciliationStorage.ExistingKeys.Add(operation.StorageKey);
        var job = new PurgeCandidateDocumentStorageReconciliationJob(
            db, reconciliationStorage,
            Options.Create(new CandidateDocumentUploadOptions { UploadIntentGracePeriodMinutes = 0 }),
            new FakeClock(FixedUtcNow.AddHours(2)), new FakeAuditPublisher(), new FakeLegalHoldStatusReader(),
            new RecordingBackgroundJobClient(), NullLogger<PurgeCandidateDocumentStorageReconciliationJob>.Instance);

        await job.ExecuteAsync();

        var reconciled = await db.CandidateDocumentDeletionOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(CandidateDocumentDeletionOperation.StatusProcessing, reconciled.Status);
    }

    [Fact]
    public async Task HandleAsync_DbSave_Fails_And_Both_Delete_And_Durable_Persistence_Fail_Still_Rethrows_Original_Exception()
    {
        var storage = new FakeCandidateDocumentStorageService { ThrowOnNextDeleteAttempts = 1 };
        var companyId = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ThrowOnceRecruitmentDbContext(options, throwFromCallNumber: 2);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.BaseSaveChangesAsync();

        var handler = BuildHandler(db, storage);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(
                new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile() },
                Guid.NewGuid(),
                CancellationToken.None));

        Assert.Equal("Simulated database failure.", thrown.Message);
        Assert.Empty(storage.Deletions);

        var operation = await db.CandidateDocumentDeletionOperations.SingleAsync();
        Assert.Equal(CandidateDocumentDeletionOperation.StatusReserved, operation.Status);
        Assert.Null(operation.ConfirmedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_And_Does_Not_Upload_Or_Create_Row_For_Purged_Candidate()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var candidate = await SeedCandidate(db, companyId);
        candidate.Purge(Guid.NewGuid(), Now);
        await db.SaveChangesAsync();
        var handler = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            new UploadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, Title = "Resume", File = FakePdfFile() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);

        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.CandidateDocuments.ToListAsync());
    }

    private sealed class CancellingOnSaveRecruitmentDbContext(
        DbContextOptions<RecruitmentDbContext> options, CancellationTokenSource requestCts)
        : RecruitmentDbContext(options)
    {
        private int _callCount;

        public Task<int> BaseSaveChangesAsync(CancellationToken ct = default) =>
            base.SaveChangesAsync(ct);

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == 1)
                return base.SaveChangesAsync(cancellationToken);

            requestCts.Cancel();
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class ThrowOnceRecruitmentDbContext(
        DbContextOptions<RecruitmentDbContext> options, int throwFromCallNumber = 2)
        : RecruitmentDbContext(options)
    {
        private int _callCount;

        public Task<int> BaseSaveChangesAsync(CancellationToken ct = default) =>
            base.SaveChangesAsync(ct);

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == throwFromCallNumber)
                return Task.FromException<int>(new DbUpdateException("Simulated database failure."));

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
