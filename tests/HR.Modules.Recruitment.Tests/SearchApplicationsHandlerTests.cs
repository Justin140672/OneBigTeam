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

    // ----- Internal recruitment Ticket 6: IsInternal / EmployeeId / CandidateId / stage name -----

    private sealed record MixedSeed(
        Guid CompanyId,
        Guid EmployeeId,
        Guid InternalId,
        Guid InternalCandidateId,
        Guid DirectId,
        Guid LegacyNullSourceId,
        Guid HiredExternalId);

    private static async Task<MixedSeed> SeedMixedAsync(RecruitmentDbContext db)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacA = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Role A", null, Guid.NewGuid(), Now);
        var vacB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Role B", null, Guid.NewGuid(), Now);
        db.Vacancies.AddRange(vacA, vacB);

        var (internalCandidate, internalApp) = InternalApplicationTestData.AddInternal(db, companyId, vacA.Id, stages.ApplicationReceived.Id, employeeId, Now);
        var (_, directApp) = InternalApplicationTestData.AddExternal(db, companyId, vacA.Id, stages.CvReview.Id, ApplicationSource.Direct, Now.AddMinutes(1));
        var (_, legacyApp) = InternalApplicationTestData.AddExternal(db, companyId, vacB.Id, stages.ApplicationReceived.Id, null, Now.AddMinutes(2), "Noah", "Patel");
        var (_, hiredApp)  = InternalApplicationTestData.AddHiredExternal(db, companyId, vacB.Id, stages.Hired.Id, ApplicationSource.Direct, Guid.NewGuid(), Now.AddMinutes(3));
        await db.SaveChangesAsync();

        return new MixedSeed(companyId, employeeId, internalApp.Id, internalCandidate.Id, directApp.Id, legacyApp.Id, hiredApp.Id);
    }

    [Fact]
    public async Task Populates_IsInternal_And_EmployeeId_Only_For_Internal_Applications()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = seed.CompanyId },
            CancellationToken.None);

        var items = result.Items.ToDictionary(i => i.ApplicationId);
        Assert.Equal(4, items.Count);

        Assert.True(items[seed.InternalId].IsInternal);
        Assert.Equal(seed.EmployeeId, items[seed.InternalId].EmployeeId);

        foreach (var externalId in new[] { seed.DirectId, seed.LegacyNullSourceId, seed.HiredExternalId })
        {
            Assert.False(items[externalId].IsInternal);
            Assert.Null(items[externalId].EmployeeId);
        }
    }

    [Fact]
    public async Task IsInternal_True_Returns_Only_Internal_Applications()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = seed.CompanyId, IsInternal = true },
            CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        var item = Assert.Single(result.Items);
        Assert.Equal(seed.InternalId, item.ApplicationId);
        Assert.Equal(seed.EmployeeId, item.EmployeeId);
    }

    [Fact]
    public async Task IsInternal_False_Returns_External_Including_Legacy_Null_Source_And_Hired_External()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = seed.CompanyId, IsInternal = false },
            CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(
            new[] { seed.DirectId, seed.LegacyNullSourceId, seed.HiredExternalId }.OrderBy(x => x),
            result.Items.Select(i => i.ApplicationId).OrderBy(x => x));
        Assert.All(result.Items, i =>
        {
            Assert.False(i.IsInternal);
            Assert.Null(i.EmployeeId);
        });
    }

    [Fact]
    public async Task IsInternal_Null_Returns_All_Applications()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = seed.CompanyId, IsInternal = null },
            CancellationToken.None);

        Assert.Equal(4, result.TotalCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Direct")]
    public async Task Hired_External_Candidate_Linked_To_Employee_Is_Not_Internal(string? source)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        var (candidate, hired) = InternalApplicationTestData.AddHiredExternal(
            db, companyId, vacancy.Id, stages.Hired.Id, InternalApplicationTestData.ParseSource(source), Guid.NewGuid(), Now);
        await db.SaveChangesAsync();
        Assert.NotNull(candidate.EmployeeId);

        var internalOnly = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId, IsInternal = true }, CancellationToken.None);
        var all = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(0, internalOnly.TotalCount);
        var item = Assert.Single(all.Items);
        Assert.Equal(hired.Id, item.ApplicationId);
        Assert.False(item.IsInternal);
        Assert.Null(item.EmployeeId);
    }

    [Fact]
    public async Task Filter_By_CandidateId_Returns_Only_That_Candidates_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacA = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Role A", null, Guid.NewGuid(), Now);
        var vacB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Role B", null, Guid.NewGuid(), Now);
        var alice = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@ex.com", null, null, Now);
        var bob   = Candidate.Create(Guid.NewGuid(), companyId, "Bob",   "Jones", "bob@ex.com",   null, null, Now);
        var aliceA = Application.Create(Guid.NewGuid(), companyId, vacA.Id, alice.Id, stages.ApplicationReceived.Id, null, Now);
        var aliceB = Application.Create(Guid.NewGuid(), companyId, vacB.Id, alice.Id, stages.CvReview.Id, null, Now.AddMinutes(1));
        db.Vacancies.AddRange(vacA, vacB);
        db.Candidates.AddRange(alice, bob);
        db.Applications.AddRange(
            aliceA,
            aliceB,
            Application.Create(Guid.NewGuid(), companyId, vacA.Id, bob.Id, stages.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId, CandidateId = alice.Id },
            CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, i => Assert.Equal(alice.Id, i.CandidateId));
        Assert.Equal(
            new[] { aliceA.Id, aliceB.Id }.OrderBy(x => x),
            result.Items.Select(i => i.ApplicationId).OrderBy(x => x));
    }

    [Fact]
    public async Task Filter_By_CandidateId_From_Another_Company_Returns_Nothing()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var stagesB  = RecruitmentStageTestData.AddDefaultStages(db, companyB, Now);
        var vacB     = Vacancy.Create(Guid.NewGuid(), companyB, Guid.NewGuid(), "Job B", null, Guid.NewGuid(), Now);
        var cB       = Candidate.Create(Guid.NewGuid(), companyB, "Bob", "Jones", "b@ex.com", null, null, Now);
        db.Vacancies.Add(vacB);
        db.Candidates.Add(cB);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyB, vacB.Id, cB.Id, stagesB.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyA, CandidateId = cB.Id },
            CancellationToken.None);

        Assert.Equal(0, result.TotalCount);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Filter_By_CandidateId_Combines_With_IsInternal()
    {
        await using var db = BuildContext();
        var seed = await SeedMixedAsync(db);

        var internalResult = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = seed.CompanyId, CandidateId = seed.InternalCandidateId, IsInternal = true },
            CancellationToken.None);
        var externalResult = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = seed.CompanyId, CandidateId = seed.InternalCandidateId, IsInternal = false },
            CancellationToken.None);

        Assert.Equal(seed.InternalId, Assert.Single(internalResult.Items).ApplicationId);
        Assert.Empty(externalResult.Items);
    }

    [Fact]
    public async Task Populates_CurrentStageName_And_IsWithdrawn()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var c1 = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@ex.com", null, null, Now);
        var c2 = Candidate.Create(Guid.NewGuid(), companyId, "Bob",   "Jones", "bob@ex.com",   null, null, Now);
        var active    = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c1.Id, stages.CvReview.Id, null, Now);
        var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, c2.Id, stages.Interview.Id, null, Now);
        withdrawn.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(c1, c2);
        db.Applications.AddRange(active, withdrawn);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId },
            CancellationToken.None);

        var items = result.Items.ToDictionary(i => i.ApplicationId);
        Assert.Equal("CV Review", items[active.Id].CurrentStageName);
        Assert.False(items[active.Id].IsWithdrawn);
        Assert.Equal("Interview", items[withdrawn.Id].CurrentStageName);
        Assert.True(items[withdrawn.Id].IsWithdrawn);
    }

    [Fact]
    public async Task CurrentStageName_Is_Not_Resolved_From_Another_Companys_Stage()
    {
        // The stage lookup is company-scoped; an application whose stage id belongs to another
        // company's stage row must not pick up that company's stage name.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var otherStages = RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Alice", "Smith", "alice@ex.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, otherStages.CvReview.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new SearchApplicationsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Null(Assert.Single(result.Items).CurrentStageName);
    }

    private static SearchApplicationsHandler Handler(RecruitmentDbContext db) =>
        new(db, new FakePositionProfileReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
