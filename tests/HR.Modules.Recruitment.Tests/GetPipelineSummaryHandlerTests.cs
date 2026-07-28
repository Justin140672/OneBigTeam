using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetPipelineSummary;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetPipelineSummaryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_No_Stages_When_Company_Has_No_RecruitmentStages()
    {
        await using var db = BuildContext();
        var handler = new GetPipelineSummaryHandler(db);

        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Returns_Active_NonTerminal_Stages_Zero_Filled_When_No_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        await db.SaveChangesAsync();

        var handler = new GetPipelineSummaryHandler(db);
        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(companyId), CancellationToken.None);

        Assert.Equal(
            ["Application Received", "CV Review", "Interview", "Offer"],
            result.Items.Select(i => i.Status).ToArray());
        Assert.All(result.Items, i => Assert.Equal(0, i.ApplicationCount));
    }

    [Fact]
    public async Task HandleAsync_Groups_By_Stage_And_Zero_Fills_Stages_With_No_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var (vacancy, candidatePool) = SeedVacancyAndCandidates(db, companyId, 3);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidatePool[0].Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidatePool[1].Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidatePool[2].Id, stages.CvReview.Id, null, Now));
        await db.SaveChangesAsync();

        var handler = new GetPipelineSummaryHandler(db);
        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(companyId), CancellationToken.None);

        var byStatus = result.Items.ToDictionary(i => i.Status, i => i.ApplicationCount);
        Assert.Equal(2, byStatus["Application Received"]);
        Assert.Equal(1, byStatus["CV Review"]);
        Assert.Equal(0, byStatus["Interview"]);
        Assert.Equal(0, byStatus["Offer"]);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Terminal_Stages_And_Withdrawn_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var (vacancy, candidatePool) = SeedVacancyAndCandidates(db, companyId, 3);

        var applied = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidatePool[0].Id, stages.ApplicationReceived.Id, null, Now);
        var hired = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidatePool[1].Id, stages.Hired.Id, null, Now);
        var withdrawn = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidatePool[2].Id, stages.ApplicationReceived.Id, null, Now);
        withdrawn.Withdraw(Now);

        db.Applications.AddRange(applied, hired, withdrawn);
        await db.SaveChangesAsync();

        var handler = new GetPipelineSummaryHandler(db);
        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(companyId), CancellationToken.None);

        Assert.Equal(4, result.Items.Count);
        Assert.DoesNotContain(result.Items, i => i.Status is "Hired" or "Rejected");
        Assert.Equal(1, result.Items.Sum(i => i.ApplicationCount));
    }

    [Fact]
    public async Task HandleAsync_Excludes_Inactive_Stages()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        stages.Offer.SetActiveStatus(false, Now);
        await db.SaveChangesAsync();

        var handler = new GetPipelineSummaryHandler(db);
        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(companyId), CancellationToken.None);

        Assert.DoesNotContain(result.Items, i => i.Status == "Offer");
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var otherStages = RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);
        var (vacancy, candidatePool) = SeedVacancyAndCandidates(db, companyId, 1);
        var (otherVacancy, otherCandidatePool) = SeedVacancyAndCandidates(db, otherCompanyId, 1);

        db.Applications.AddRange(
            Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidatePool[0].Id, stages.ApplicationReceived.Id, null, Now),
            Application.Create(Guid.NewGuid(), otherCompanyId, otherVacancy.Id, otherCandidatePool[0].Id, otherStages.ApplicationReceived.Id, null, Now));
        await db.SaveChangesAsync();

        var handler = new GetPipelineSummaryHandler(db);
        var result = await handler.HandleAsync(new GetPipelineSummaryRequest(companyId), CancellationToken.None);

        var applied = Assert.Single(result.Items, i => i.Status == "Application Received");
        Assert.Equal(1, applied.ApplicationCount);
    }

    private static (Vacancy Vacancy, List<Candidate> Candidates) SeedVacancyAndCandidates(
        RecruitmentDbContext db, Guid companyId, int candidateCount)
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);

        var candidates = new List<Candidate>();
        for (var i = 0; i < candidateCount; i++)
        {
            var candidate = Candidate.Create(
                Guid.NewGuid(), companyId, "First", $"Last{i}", $"candidate{i}.{Guid.NewGuid():N}@example.com", null, null, Now);
            candidates.Add(candidate);
        }
        db.Candidates.AddRange(candidates);

        return (vacancy, candidates);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
