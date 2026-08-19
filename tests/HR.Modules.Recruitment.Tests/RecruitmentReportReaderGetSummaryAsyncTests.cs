using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Covers RecruitmentReportReader.GetSummaryAsync (the IRecruitmentPipelineSummaryReader
/// implementation). Other members of RecruitmentReportReader (GetByRecruiterAsync,
/// GetByVacancyAsync, GetVacancyPerformanceAsync) are exercised via their handler tests
/// elsewhere.
/// </summary>
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
            Guid.NewGuid(), companyId, "First", $"Last{seed}", $"candidate{seed}.{Guid.NewGuid():N}@example.com", null, null, Now);
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
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, CancellationToken.None);

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
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, CancellationToken.None);

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
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, CancellationToken.None);

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
        var result = await reader.GetSummaryAsync(companyId, includeClosed: true, CancellationToken.None);

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
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, CancellationToken.None);

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
                positionProfileId, "Software Engineer", Guid.NewGuid(), null, true, null, null, "Engineering"),
        };
        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader(summaries: summaries));

        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, CancellationToken.None);

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
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, CancellationToken.None);

        var row = Assert.Single(result.Vacancies);
        Assert.Equal("Mine", row.VacancyTitle);
    }

    [Fact]
    public async Task GetSummaryAsync_Returns_Empty_Vacancies_But_Populated_Stages_When_Company_Has_No_Vacancies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var reader = new RecruitmentReportReader(db, new FakePositionProfileReader());
        var result = await reader.GetSummaryAsync(companyId, includeClosed: false, CancellationToken.None);

        Assert.Empty(result.Vacancies);
        Assert.NotEmpty(result.Stages);
    }
}
