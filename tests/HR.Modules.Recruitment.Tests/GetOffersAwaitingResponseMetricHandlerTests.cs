using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetOffersAwaitingResponseMetric;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;

namespace HR.Modules.Recruitment.Tests;

public class GetOffersAwaitingResponseMetricHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Counts_Live_Apps_In_Single_Offer_Stage()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 3);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.Offer.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.Offer.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[2].Id, stages.Interview.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.True(result.OfferStageConfigured);
        Assert.Equal(2, result.Count);
        Assert.Equal(result.Count, result.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Sums_Across_Multiple_Offer_Purpose_Stages()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var verbal = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Verbal offer", 4, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);
        var written = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Written offer", 5, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Offer);
        var placed = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Placed", 6, true, RecruitmentStageTerminalOutcome.Hired, Now);
        db.RecruitmentStages.AddRange(verbal, written, placed);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 3);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, verbal.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, written.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[2].Id, written.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.True(result.OfferStageConfigured);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_Zero_And_NotConfigured_When_No_Offer_Purpose_Stage()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now, withPurposes: false);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 1);
        db.Applications.Add(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.Offer.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.False(result.OfferStageConfigured);
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Fall_Back_To_Last_Ordered_Stage()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        // Last non-terminal stage by order carries a *different* purpose — must not be counted as offers.
        var screen = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Screen", 1, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.NewApplication);
        var finalRound = RecruitmentStage.Create(Guid.NewGuid(), companyId, "Final round", 2, false, RecruitmentStageTerminalOutcome.None, Now, RecruitmentStagePurpose.Interview);
        db.RecruitmentStages.AddRange(screen, finalRound);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 1);
        db.Applications.Add(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, finalRound.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.False(result.OfferStageConfigured);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Withdrawn_Apps()
    {
        await using var db = GetNewApplicationsMetricHandlerTests.BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var (vacancy, candidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, companyId, 2);

        var live = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.Offer.Id, null, Now);
        var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[1].Id, stages.Offer.Id, null, Now);
        withdrawn.Withdraw(Now);
        db.Applications.AddRange(live, withdrawn);
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
        var (otherVacancy, otherCandidates) = GetNewApplicationsMetricHandlerTests.SeedVacancyAndCandidates(db, otherCompanyId, 1);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidates[0].Id, stages.Offer.Id, null, Now),
            Application.Create(Guid.NewGuid(), otherCompanyId, otherVacancy.Id, otherCandidates[0].Id, otherStages.Offer.Id, null, Now));
        await db.SaveChangesAsync();

        var result = await Handle(db, companyId);

        Assert.Equal(1, result.Count);
    }

    private static async Task<GetOffersAwaitingResponseMetricResponse> Handle(RecruitmentDbContext db, Guid companyId)
    {
        var handler = new GetOffersAwaitingResponseMetricHandler(db, new FakePositionProfileReader());
        return await handler.HandleAsync(new GetOffersAwaitingResponseMetricRequest { CompanyId = companyId }, CancellationToken.None);
    }
}
