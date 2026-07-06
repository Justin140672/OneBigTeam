using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.DeleteCandidateDocument;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class DeleteCandidateDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Removes_Document_And_Deletes_From_Storage()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var document = CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", "storage/key/resume.pdf", Guid.NewGuid(), Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.Add(document);
        await db.SaveChangesAsync();

        var result = await new DeleteCandidateDocumentHandler(db, storage).HandleAsync(
            new DeleteCandidateDocumentRequest { CompanyId = companyId, CandidateId = candidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.CandidateDocuments.ToListAsync());
        Assert.Single(storage.Deletions);
        Assert.Equal(document.StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Missing()
    {
        await using var db = BuildContext();
        var storage = new FakeCandidateDocumentStorageService();

        var result = await new DeleteCandidateDocumentHandler(db, storage).HandleAsync(
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

        var result = await new DeleteCandidateDocumentHandler(db, storage).HandleAsync(
            new DeleteCandidateDocumentRequest { CompanyId = companyId, CandidateId = otherCandidate.Id, DocumentId = document.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
