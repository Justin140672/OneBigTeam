using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetNewApplicationsMetric;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetNewApplicationsMetricHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Purpose_Path_Counts_Live_Apps_In_NewApplication_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var (vacancy, candidates) = SeedVacancyAndCandidates(db, companyId, 3);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.ApplicationReceived.Id, null, Now.AddDays(-40)),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.ApplicationReceived.Id, null, Now.AddDays(-1)),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[2].Id, stages.CvReview.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.True(result.DefinedByStagePurpose);
        Assert.Equal(2, result.Count);
        Assert.Equal(result.Count, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Purpose_Path_Sums_Across_Multiple_NewApplication_Stages()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var secondNew = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Recruiter Screen", 7, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.NewApplication);
        db.RecruitmentStages.Add(secondNew);
        var (vacancy, candidates) = SeedVacancyAndCandidates(db, companyId, 2);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, secondNew.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.True(result.DefinedByStagePurpose);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task HandleAsync_Purpose_Path_Excludes_Withdrawn_And_Terminal_Stage_Apps()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var (vacancy, candidates) = SeedVacancyAndCandidates(db, companyId, 3);

        var live = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.ApplicationReceived.Id, null, Now);
        var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.ApplicationReceived.Id, null, Now);
        withdrawn.Withdraw(Now);
        // Terminal stage app: even though nominally "new", it has reached a terminal stage.
        var hired = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[2].Id, stages.Hired.Id, null, Now);

        db.Applications.AddRange(live, withdrawn, hired);
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
        Assert.Equal(live.Id, result.Items[0].ApplicationId);
    }

    [Fact]
    public async Task HandleAsync_Fallback_Path_Uses_Default_14_Day_Window_When_No_Purpose_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now, withPurposes: false);
        var (vacancy, candidates) = SeedVacancyAndCandidates(db, companyId, 2);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.ApplicationReceived.Id, null, Now.AddDays(-10)),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.ApplicationReceived.Id, null, Now.AddDays(-20)));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.False(result.DefinedByStagePurpose);
        Assert.Equal(1, result.Count);
        Assert.Equal(result.Count, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Fallback_Path_Respects_Custom_NewWithinDays()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now, withPurposes: false);
        var (vacancy, candidates) = SeedVacancyAndCandidates(db, companyId, 2);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.ApplicationReceived.Id, null, Now.AddDays(-10)),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.ApplicationReceived.Id, null, Now.AddDays(-20)));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId, newWithinDays: 30);

        Assert.False(result.DefinedByStagePurpose);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task HandleAsync_Fallback_Path_Still_Excludes_Withdrawn_And_Terminal()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now, withPurposes: false);
        var (vacancy, candidates) = SeedVacancyAndCandidates(db, companyId, 3);

        var live = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.ApplicationReceived.Id, null, Now);
        var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.ApplicationReceived.Id, null, Now);
        withdrawn.Withdraw(Now);
        var rejected = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[2].Id, stages.Rejected.Id, null, Now);

        db.Applications.AddRange(live, withdrawn, rejected);
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var otherStages = RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);
        var (vacancy, candidates) = SeedVacancyAndCandidates(db, companyId, 1);
        var (otherVacancy, otherCandidates) = SeedVacancyAndCandidates(db, otherCompanyId, 1);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), otherCompanyId, otherVacancy.Id, otherCandidates[0].Id, otherStages.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
    }

    private static async Task<GetNewApplicationsMetricResponse> Handle(RecruitmentDbContext db, Guid companyId, int? newWithinDays = null)
    {
        var handler = new GetNewApplicationsMetricHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        return await handler.HandleAsync(
            new GetNewApplicationsMetricRequest { CompanyId = companyId, NewWithinDays = newWithinDays },
            CancellationToken.None);
    }

    internal static (Vacancy Vacancy, List<Candidate> Candidates) SeedVacancyAndCandidates(
        RecruitmentDbContext db, Guid companyId, int candidateCount)
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);

        var candidates = new List<Candidate>();
        for (var i = 0; i < candidateCount; i++)
        {
            candidates.Add(Candidate.Create(
                Guid.NewGuid(), companyId, "First", $"Last{i}", $"candidate{i}.{Guid.NewGuid():N}@example.com", null, null, Now));
        }
        db.Candidates.AddRange(candidates);
        return (vacancy, candidates);
    }

    internal static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
