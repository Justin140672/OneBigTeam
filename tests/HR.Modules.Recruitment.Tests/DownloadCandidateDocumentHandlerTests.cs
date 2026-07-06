using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DownloadCandidateDocument;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class DownloadCandidateDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Returns_DownloadUrl_Derived_From_StorageKey()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var document = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", "storage/key/resume.pdf", Guid.NewGuid(), Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await new DownloadCandidateDocumentHandler(db, storage).HandleAsync(
            new DownloadCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(document.StorageKey, result.Value!.ToString());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Missing()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();

        var result = await new DownloadCandidateDocumentHandler(db, storage).HandleAsync(
            new DownloadCandidateDocumentRequest { CompanyId = Guid.NewGuid(), CandidateId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
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

        var result = await new DownloadCandidateDocumentHandler(db, storage).HandleAsync(
            new DownloadCandidateDocumentRequest { CompanyId = companyId, CandidateId = otherCandidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
