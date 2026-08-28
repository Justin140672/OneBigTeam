using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.SearchApplications;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class SearchApplicationsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Returns_All_Applications_When_No_Filters()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var c1 = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@ex.com", null, null, Now);
        var c2 = Candidate.Create(Guid.NewGuid(), companyId, "Bob",   "Jones", "bob@ex.com",   null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(c1, c2);
        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c1.Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c2.Id, stages.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task Search_Filters_By_Candidate_Name()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var alice = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith",   "alice@ex.com", null, null, Now);
        var bob   = Candidate.Create(Guid.NewGuid(), companyId, "Bob",   "Johnson", "bob@ex.com",   null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(alice, bob);
        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, alice.Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, bob.Id,   stages.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId, Search = "alice" },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Alice Smith", result.Items[0].CandidateName);
    }

    [Fact]
    public async Task Filter_By_VacancyId_Returns_Only_That_Vacancys_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacA = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Role A", null, Guid.NewGuid(), Now);
        var vacB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Role B", null, Guid.NewGuid(), Now);
        var c1   = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "a@ex.com", null, null, Now);
        var c2   = Candidate.Create(Guid.NewGuid(), companyId, "Bob",   "Jones", "b@ex.com", null, null, Now);
        db.Vacancies.AddRange(vacA, vacB);
        db.Candidates.AddRange(c1, c2);
        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacA.Id, c1.Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacB.Id, c2.Id, stages.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId, VacancyId = vacA.Id },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(vacA.Id, result.Items[0].VacancyId);
    }

    [Fact]
    public async Task Filter_By_StageId_Returns_Only_That_Stages_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages  = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var c1      = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "a@ex.com", null, null, Now);
        var c2      = Candidate.Create(Guid.NewGuid(), companyId, "Bob",   "Jones", "b@ex.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(c1, c2);
        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c1.Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c2.Id, stages.CvReview.Id,            null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId, StageId = stages.ApplicationReceived.Id },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(stages.ApplicationReceived.Id, result.Items[0].CurrentStageId);
    }

    [Fact]
    public async Task Filter_By_AppliedFrom_Excludes_Earlier_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages    = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy   = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var c1        = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "a@ex.com", null, null, Now);
        var c2        = Candidate.Create(Guid.NewGuid(), companyId, "Bob",   "Jones", "b@ex.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(c1, c2);
        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c1.Id, stages.ApplicationReceived.Id, null, Now.AddDays(-10)),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c2.Id, stages.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId, AppliedFrom = Now.AddDays(-1) },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Bob Jones", result.Items[0].CandidateName);
    }

    [Fact]
    public async Task Isolates_By_Company()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var stagesA  = RecruitmentStageTestData.AddDefaultStages(db, companyA, Now);
        var stagesB  = RecruitmentStageTestData.AddDefaultStages(db, companyB, Now);
        var vacA     = Vacancy.Create(Guid.NewGuid(), companyA, Guid.NewGuid(), "Job A", null, Guid.NewGuid(), Now);
        var vacB     = Vacancy.Create(Guid.NewGuid(), companyB, Guid.NewGuid(), "Job B", null, Guid.NewGuid(), Now);
        var cA       = Candidate.Create(Guid.NewGuid(), companyA, "Alice", "Smith", "a@ex.com", null, null, Now);
        var cB       = Candidate.Create(Guid.NewGuid(), companyB, "Bob",   "Jones", "b@ex.com", null, null, Now);
        db.Vacancies.AddRange(vacA, vacB);
        db.Candidates.AddRange(cA, cB);
        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyA, vacA.Id, cA.Id, stagesA.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyB, vacB.Id, cB.Id, stagesB.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal("Alice Smith", i.CandidateName));
    }

    [Fact]
    public async Task Paging_Returns_Correct_Page()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages    = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy   = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var candidates = Enumerable.Range(1, 5)
            .Select(i => Candidate.Create(Guid.NewGuid(), companyId, $"Candidate{i}", "Test", $"c{i}@ex.com", null, null, Now))
            .ToList();
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidates);
        db.Applications.AddRange(candidates.Select(c =>
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c.Id, stages.ApplicationReceived.Id, null, Now)));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId, PageNumber = 2, PageSize = 2 },
            CancellationToken.None);

        Assert.Equal(5, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.PageNumber);
        Assert.Equal(3, result.TotalPages);
    }

    private static SearchApplicationsHandler Handler(RecruitmentDbContext db) =>
        new(db, new FakePositionProfileReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
