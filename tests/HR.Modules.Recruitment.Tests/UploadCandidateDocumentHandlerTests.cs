using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UploadCandidateDocument;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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
            new FakeClock(FixedUtcNow));

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
        await using var db = new ThrowingRecruitmentDbContext(options);
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
    }

    private sealed class ThrowingRecruitmentDbContext(DbContextOptions<RecruitmentDbContext> options)
        : RecruitmentDbContext(options)
    {
        public Task<int> BaseSaveChangesAsync(CancellationToken ct = default) =>
            base.SaveChangesAsync(ct);

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database failure.");
    }
}
