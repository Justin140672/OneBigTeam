using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetCandidatesInProgressMetric;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;

namespace HR.Modules.Recruitment.Tests;

public class GetCandidatesInProgressMetricHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Counts_Live_Apps_Across_All_NonTerminal_Stages()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 4);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.CvReview.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[2].Id, stages.Interview.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[3].Id, stages.Offer.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(4, result.Count);
        Assert.Equal(result.Count, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Works_With_Custom_Renamed_Pipeline()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var screening = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Screening call", 1, false, RecruitmentStageTerminalOutcome.None, Now);
        var techTest = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Take-home task", 2, false, RecruitmentStageTerminalOutcome.None, Now);
        var placed = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Placed", 3, true, RecruitmentStageTerminalOutcome.Hired, Now);
        db.RecruitmentStages.AddRange(screening, techTest, placed);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 3);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, screening.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, techTest.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[2].Id, placed.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Withdrawn_And_Terminal_Stage_Apps()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 4);

        var live = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.Interview.Id, null, Now);
        var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.Interview.Id, null, Now);
        withdrawn.Withdraw(Now);
        var hired = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[2].Id, stages.Hired.Id, null, Now);
        var rejected = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[3].Id, stages.Rejected.Id, null, Now);

        db.Applications.AddRange(live, withdrawn, hired, rejected);
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
        Assert.Equal(live.Id, result.Items[0].ApplicationId);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var otherStages = RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 1);
        var (otherVacancy, otherCandidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, otherCompanyId, 2);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.CvReview.Id, null, Now),
            Application.Create(Guid.NewGuid(), otherCompanyId, otherVacancy.Id, otherCandidates[0].Id, otherStages.CvReview.Id, null, Now),
            Application.Create(Guid.NewGuid(), otherCompanyId, otherVacancy.Id, otherCandidates[1].Id, otherStages.Interview.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
    }

    private static async Task<GetCandidatesInProgressMetricResponse> Handle(RecruitmentDbContext db, Guid companyId)
    {
        var handler = new GetCandidatesInProgressMetricHandler(db, new FakePositionProfileReader());
        return await handler.HandleAsync(new GetCandidatesInProgressMetricRequest { CompanyId = companyId }, CancellationToken.None);
    }
}
