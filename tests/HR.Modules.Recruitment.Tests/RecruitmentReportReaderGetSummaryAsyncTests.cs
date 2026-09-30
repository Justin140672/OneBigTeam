using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class RecruitmentReportReaderGetSummaryAsyncTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static Vacancy SeedOpenVacancy(RecruitmentDbContext db, Guid companyId, Guid positionProfileId, string? title = "Engineer")
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, title, null, Guid.NewGuid(), Now);
        vacancy.Open(Now, new DateOnly(2026, 1, 1));
        db.Vacancies.Add(vacancy);
        return vacancy;
    }

    private static Candidate SeedCandidate(RecruitmentDbContext db, Guid companyId, int seed)
    {
        var candidate = Candidate.Create(
            Guid.NewGuid(), companyId, "First", $"Last{seed}", $"candidate{seed}.{Guid.NewGuid():N}@example.com", null, Now);
        db.Candidates.Add(candidate);
        return candidate;
    }

    [Fact]
    public async Task GetSummaryAsync_Returns_Stage_Columns_From_Companys_Configured_RecruitmentStages()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, isInternal: null, CancellationToken.None);

        Assert.Equal(
            ["Application Received", "CV Review", "Interview", "Offer", "Hired", "Rejected"],
            result.Stages.Select(s => s.StageName).ToArray());
    }

    [Fact]
    public async Task GetSummaryAsync_Excludes_Inactive_RecruitmentStages_From_Columns()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        stages.Offer.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, isInternal: null, CancellationToken.None);

        Assert.DoesNotContain(result.Stages, s => s.StageName == "Offer");
    }

    [Fact]
    public async Task GetSummaryAsync_Excludes_Closed_And_Cancelled_Vacancies_By_Default()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);

        var openVacancy = SeedOpenVacancy(db, companyId, positionProfileId, "Open Role");

        var closedVacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Closed Role", null, Guid.NewGuid(), Now);
        closedVacancy.Open(Now, new DateOnly(2026, 1, 1));
        closedVacancy.Close(Now, new DateOnly(2026, 2, 1));
        db.Vacancies.Add(closedVacancy);

        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, isInternal: null, CancellationToken.None);

        Assert.Single(result.Vacancies);
        Assert.Equal("Open Role", result.Vacancies[0].VacancyTitle);
    }

    [Fact]
    public async Task GetSummaryAsync_Includes_Closed_Vacancies_When_IncludeClosed_Is_True()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);

        var closedVacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Closed Role", null, Guid.NewGuid(), Now);
        closedVacancy.Open(Now, new DateOnly(2026, 1, 1));
        closedVacancy.Close(Now, new DateOnly(2026, 2, 1));
        db.Vacancies.Add(closedVacancy);

        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: true, isInternal: null, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal("Closed Role", row.VacancyTitle);
        Assert.Equal("Closed", row.Status);
    }

    [Fact]
    public async Task GetSummaryAsync_Counts_Candidates_By_Current_Stage_Per_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = SeedOpenVacancy(db, companyId, positionProfileId);

        var candidateA = SeedCandidate(db, companyId, 1);
        var candidateB = SeedCandidate(db, companyId, 2);
        var candidateC = SeedCandidate(db, companyId, 3);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateA.Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateB.Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateC.Id, stages.Interview.Id, null, Now));

        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, isInternal: null, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal(3, row.CandidateCount);
        Assert.Equal(2, row.CandidatesByStage[stages.ApplicationReceived.Id]);
        Assert.Equal(1, row.CandidatesByStage[stages.Interview.Id]);
        Assert.False(row.CandidatesByStage.ContainsKey(stages.Offer.Id));
    }

    [Fact]
    public async Task GetSummaryAsync_Resolves_Department_And_Position_Title_Via_PositionProfileReader()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = SeedOpenVacancy(db, companyId, positionProfileId, title: null);

        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(
                positionProfileId, "Software Engineer", Guid.NewGuid(), true, null, null, "Engineering"),
        };
        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader(summaries: summaries));

        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, isInternal: null, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal("Software Engineer", row.PositionProfileTitle);
        Assert.Equal("Engineering", row.DepartmentName);
    }

    [Fact]
    public async Task GetSummaryAsync_Isolates_By_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);

        SeedOpenVacancy(db, companyId, Guid.NewGuid(), "Mine");
        SeedOpenVacancy(db, otherCompanyId, Guid.NewGuid(), "TheirsNotMine");

        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, isInternal: null, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal("Mine", row.VacancyTitle);
    }

    // ----- Internal recruitment Ticket 6: isInternal filter -----

    private sealed record InternalFilterSeed(Guid CompanyId, RecruitmentStageTestData.SeededStages Stages);

    private static async Task<InternalFilterSeed> SeedInternalAndExternalAsync(RecruitmentDbContext db)
    {
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = SeedOpenVacancy(db, companyId, Guid.NewGuid());

        InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, Guid.NewGuid(), Now);
        InternalApplicationTestData.AddInternal(db, companyId, vacancy.Id, stages.Interview.Id, Guid.NewGuid(), Now, "Tom", "Baker");
        InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, ApplicationSource.Direct, Now);
        InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.Interview.Id, null, Now, "Noah", "Patel");
        InternalApplicationTestData.AddHiredExternal(db, companyId, vacancy.Id, stages.Hired.Id, ApplicationSource.Direct, Guid.NewGuid(), Now);
        await db.SaveChangesAsync();

        return new InternalFilterSeed(companyId, stages);
    }

    [Fact]
    public async Task GetSummaryAsync_IsInternal_Null_Counts_All_Applications()
    {
        await using var db = BuildContext();
        var seed = await SeedInternalAndExternalAsync(db);

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(seed.CompanyId, includeClosed: false, isInternal: null, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal(5, row.CandidateCount);
        Assert.Equal(2, row.CandidatesByStage[seed.Stages.ApplicationReceived.Id]);
        Assert.Equal(2, row.CandidatesByStage[seed.Stages.Interview.Id]);
        Assert.Equal(1, row.CandidatesByStage[seed.Stages.Hired.Id]);
    }

    [Fact]
    public async Task GetSummaryAsync_IsInternal_True_Counts_Only_Internal_Applications()
    {
        await using var db = BuildContext();
        var seed = await SeedInternalAndExternalAsync(db);

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(seed.CompanyId, includeClosed: false, isInternal: true, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal(2, row.CandidateCount);
        Assert.Equal(1, row.CandidatesByStage[seed.Stages.ApplicationReceived.Id]);
        Assert.Equal(1, row.CandidatesByStage[seed.Stages.Interview.Id]);
        Assert.False(row.CandidatesByStage.ContainsKey(seed.Stages.Hired.Id));
    }

    [Fact]
    public async Task GetSummaryAsync_IsInternal_False_Counts_External_Including_Legacy_And_Hired_External()
    {
        await using var db = BuildContext();
        var seed = await SeedInternalAndExternalAsync(db);

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(seed.CompanyId, includeClosed: false, isInternal: false, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal(3, row.CandidateCount);
        Assert.Equal(1, row.CandidatesByStage[seed.Stages.ApplicationReceived.Id]);
        Assert.Equal(1, row.CandidatesByStage[seed.Stages.Interview.Id]);
        Assert.Equal(1, row.CandidatesByStage[seed.Stages.Hired.Id]);
    }

    [Fact]
    public async Task GetSummaryAsync_IsInternal_True_Keeps_Vacancy_Row_With_Zero_Count_When_It_Has_No_Internal_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var vacancy = SeedOpenVacancy(db, companyId, Guid.NewGuid(), "External Only");
        InternalApplicationTestData.AddExternal(db, companyId, vacancy.Id, stages.ApplicationReceived.Id, ApplicationSource.Direct, Now);
        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, isInternal: true, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal("External Only", row.VacancyTitle);
        Assert.Equal(0, row.CandidateCount);
        Assert.Empty(row.CandidatesByStage);
    }

    [Fact]
    public async Task GetSummaryAsync_Returns_Empty_Vacancies_But_Populated_Stages_When_Company_Has_No_Vacancies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, isInternal: null, CancellationToken.None);

        Assert.Empty(result.Vacancies);
        Assert.NotEmpty(result.Stages);
    }
}
