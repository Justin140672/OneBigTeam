using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListCandidateDocuments;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
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

    [Fact]
    public async Task HandleAsync_Surfaces_Document_Kind()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(
            CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "CV", "cv.pdf", 1024, "application/pdf", "key1", Guid.NewGuid(), Now.AddMinutes(5), CandidateDocumentKind.Cv),
            CandidateDocument.Create(Guid.NewGuid(), companyId, candidate.Id, "Cover Letter", "cover.pdf", 512, "application/pdf", "key2", Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListCandidateDocumentsHandler(db).HandleAsync(
            new ListCandidateDocumentsRequest { CompanyId = companyId, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cv", result.Value!.Items[0].Kind);
        Assert.Equal("Other", result.Value.Items[1].Kind);
    }

    // ---- Internal recruitment Ticket 2: IsCurrentCv + ReferencingApplicationCount ------------------

    private static CandidateDocument Doc(
        Guid companyId, Guid candidateId, string fileName, DateTimeOffset createdAt,
        CandidateDocumentKind kind = CandidateDocumentKind.Cv) =>
        CandidateDocument.Create(
            Guid.NewGuid(), companyId, candidateId, fileName, fileName, 2048, "application/pdf",
            $"{companyId}/{candidateId}/{Guid.NewGuid():N}/{fileName}", Guid.NewGuid(), createdAt, kind);

    private static Task<Result<ListCandidateDocumentsResponse>> ListAsync(
        RecruitmentDbContext db, Guid companyId, Guid candidateId) =>
        new ListCandidateDocumentsHandler(db).HandleAsync(
            new ListCandidateDocumentsRequest { CompanyId = companyId, CandidateId = candidateId },
            CancellationToken.None);

    [Fact]
    public async Task HandleAsync_Flags_Only_The_Newest_Cv_As_Current()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var olderCv = Doc(companyId, candidate.Id, "cv-v1.pdf", Now);
        var newerCv = Doc(companyId, candidate.Id, "cv-v2.pdf", Now.AddDays(1));
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(olderCv, newerCv);
        await db.SaveChangesAsync();

        var result = await ListAsync(db, companyId, candidate.Id);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;
        Assert.Equal(2, items.Count);
        Assert.Equal(newerCv.Id, items[0].Id); // newest first
        Assert.True(items[0].IsCurrentCv);
        Assert.Equal(olderCv.Id, items[1].Id);
        Assert.False(items[1].IsCurrentCv);
    }

    [Fact]
    public async Task HandleAsync_Newer_Other_Document_Does_Not_Take_IsCurrentCv_From_Older_Cv()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var cv = Doc(companyId, candidate.Id, "cv.pdf", Now);
        var coverLetter = Doc(companyId, candidate.Id, "cover.pdf", Now.AddDays(1), CandidateDocumentKind.Other);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(cv, coverLetter);
        await db.SaveChangesAsync();

        var result = await ListAsync(db, companyId, candidate.Id);

        var items = result.Value!.Items;
        Assert.Equal(coverLetter.Id, items[0].Id); // newest first
        Assert.False(items[0].IsCurrentCv);
        Assert.Equal(cv.Id, items[1].Id);
        Assert.True(items[1].IsCurrentCv);
    }

    [Fact]
    public async Task HandleAsync_Flags_No_Document_As_Current_When_Candidate_Has_No_Cv()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(
            Doc(companyId, candidate.Id, "cover.pdf", Now, CandidateDocumentKind.Other),
            Doc(companyId, candidate.Id, "references.pdf", Now.AddDays(1), CandidateDocumentKind.Other));
        await db.SaveChangesAsync();

        var result = await ListAsync(db, companyId, candidate.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, i => Assert.False(i.IsCurrentCv));
    }

    [Fact]
    public async Task HandleAsync_Breaks_CreatedAt_Tie_By_Id_Descending_When_Choosing_Current_Cv()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var first = Doc(companyId, candidate.Id, "cv-a.pdf", Now);
        var second = Doc(companyId, candidate.Id, "cv-b.pdf", Now);
        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(first, second);
        await db.SaveChangesAsync();

        var result = await ListAsync(db, companyId, candidate.Id);

        // Identical timestamps: the higher Id sorts first and is therefore the current CV.
        // (InMemory compares Guids with the .NET comparer, the same one Max() uses here.)
        var expectedCurrentId = new[] { first.Id, second.Id }.Max();
        var items = result.Value!.Items;
        Assert.Equal(expectedCurrentId, items[0].Id);
        Assert.True(items[0].IsCurrentCv);
        Assert.False(items[1].IsCurrentCv);
    }

    [Fact]
    public async Task HandleAsync_Counts_Applications_Referencing_Each_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var oldCv = Doc(companyId, candidate.Id, "cv-v1.pdf", Now);
        var newCv = Doc(companyId, candidate.Id, "cv-v2.pdf", Now.AddDays(1));
        var unreferenced = Doc(companyId, candidate.Id, "cover.pdf", Now.AddDays(2), CandidateDocumentKind.Other);

        var applications = new List<Application>();
        for (var i = 0; i < 4; i++)
        {
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), $"Engineer {i}", null, Guid.NewGuid(), Now);
            db.Vacancies.Add(vacancy);
            applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, Now));
        }
        applications[0].AttachCv(oldCv, Now);
        applications[1].AttachCv(oldCv, Now);
        applications[2].AttachCv(newCv, Now.AddDays(1));
        // applications[3] has no submitted CV and must not be counted against any document.

        db.Candidates.Add(candidate);
        db.CandidateDocuments.AddRange(oldCv, newCv, unreferenced);
        db.Applications.AddRange(applications);
        await db.SaveChangesAsync();

        var result = await ListAsync(db, companyId, candidate.Id);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items;
        Assert.Equal(3, items.Count);
        Assert.Equal(2, items.Single(i => i.Id == oldCv.Id).ReferencingApplicationCount);
        Assert.Equal(1, items.Single(i => i.Id == newCv.Id).ReferencingApplicationCount);
        Assert.Equal(0, items.Single(i => i.Id == unreferenced.Id).ReferencingApplicationCount);
        // The older CV is retained and still referenced, but it is not the current CV.
        Assert.False(items.Single(i => i.Id == oldCv.Id).IsCurrentCv);
        Assert.True(items.Single(i => i.Id == newCv.Id).IsCurrentCv);
    }

    [Fact]
    public async Task HandleAsync_Returns_Zero_ReferencingApplicationCount_When_No_Application_References_The_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        db.Candidates.Add(candidate);
        db.Vacancies.Add(vacancy);
        // An application exists for the candidate, but no CV has been recorded against it.
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, Now));
        db.CandidateDocuments.Add(Doc(companyId, candidate.Id, "cv.pdf", Now));
        await db.SaveChangesAsync();

        var result = await ListAsync(db, companyId, candidate.Id);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(0, item.ReferencingApplicationCount);
        Assert.True(item.IsCurrentCv);
    }
}
