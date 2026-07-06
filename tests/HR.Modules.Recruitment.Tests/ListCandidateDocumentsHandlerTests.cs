using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListCandidateDocuments;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ListCandidateDocumentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    [Fact]
    public async Task HandleAsync_Returns_Documents_For_Candidate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(
            CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume", "resume.pdf", 1024, "application/pdf", "key1", Guid.NewGuid(), Now),
            CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Cover Letter", "cover.pdf", 512, "application/pdf", "key2", Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidateDocumentsHandler(db).HandleAsync(
            new ListCandidateDocumentsRequest { CompanyId = companyId, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Documents_For_Other_Candidates()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidateA = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var candidateB = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        db.Candidates.AddRange(candidateA, candidateB);
        db.CandidateDocuments.Add(
            CandidateDocument.Create(Guid.NewGuid(), companyId, candidateA.Id, "Resume", "resume.pdf", 1024, "application/pdf", "key1", Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidateDocumentsHandler(db).HandleAsync(
            new ListCandidateDocumentsRequest { CompanyId = companyId, CandidateId = candidateB.Id },
            CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_CreatedAt_Descending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(
            CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume v1", "v1.pdf", 1024, "application/pdf", "key1", Guid.NewGuid(), Now),
            CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Resume v2", "v2.pdf", 1024, "application/pdf", "key2", Guid.NewGuid(), Now.AddMinutes(5)));
        await db.SaveChangesAsync();

        var result = await new ListCandidateDocumentsHandler(db).HandleAsync(
            new ListCandidateDocumentsRequest { CompanyId = companyId, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.Equal("Resume v2", result.Value!.Items[0].Title);
    }
}
