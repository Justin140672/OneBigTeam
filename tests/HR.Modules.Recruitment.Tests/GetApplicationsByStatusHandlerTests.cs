using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetApplicationsByStatus;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetApplicationsByStatusHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Applications_For_Given_Stage_With_Candidate_And_Vacancy_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var applied = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        var otherCandidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var cvReview = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, otherCandidate.Id, stages.CvReview.Id, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidate, otherCandidate);
        db.Applications.AddRange(applied, cvReview);
        await db.SaveChangesAsync();

        var handler = new GetApplicationsByStatusHandler(db, new FakePositionProfileReader());
        var result = await handler.HandleAsync(
            new GetApplicationsByStatusRequest(companyId, stages.ApplicationReceived.Id),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(applied.Id, item.ApplicationId);
        Assert.Equal(candidate.Id, item.CandidateId);
        Assert.Equal("Emma Clarke", item.CandidateName);
        Assert.Equal("emma.clarke@example.com", item.CandidateEmail);
        Assert.Equal(vacancy.Id, item.VacancyId);
        Assert.Equal("Senior Software Engineer", item.VacancyTitle);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_AppliedAt_Descending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidateA = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var candidateB = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);

        var earlier = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateA.Id, stages.ApplicationReceived.Id, null, Now.AddDays(-2));
        var later = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateB.Id, stages.ApplicationReceived.Id, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidateA, candidateB);
        db.Applications.AddRange(earlier, later);
        await db.SaveChangesAsync();

        var handler = new GetApplicationsByStatusHandler(db, new FakePositionProfileReader());
        var result = await handler.HandleAsync(
            new GetApplicationsByStatusRequest(companyId, stages.ApplicationReceived.Id),
            CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(later.Id, result.Items[0].ApplicationId);
        Assert.Equal(earlier.Id, result.Items[1].ApplicationId);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Applications_On_A_Different_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var cvReview = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.CvReview.Id, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(cvReview);
        await db.SaveChangesAsync();

        var handler = new GetApplicationsByStatusHandler(db, new FakePositionProfileReader());
        var result = await handler.HandleAsync(
            new GetApplicationsByStatusRequest(companyId, stages.ApplicationReceived.Id),
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Isolates_By_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);

        var otherVacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var otherCandidate = Candidate.Create(Guid.NewGuid(), otherCompanyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var otherStages = RecruitmentStageTestData.AddDefaultStages(db, otherCompanyId, Now);
        var otherApplication = Application.Create(Guid.NewGuid(), otherCompanyId, otherVacancy.Id, otherCandidate.Id, otherStages.ApplicationReceived.Id, null, Now);

        db.Vacancies.AddRange(vacancy, otherVacancy);
        db.Candidates.AddRange(candidate, otherCandidate);
        db.Applications.AddRange(application, otherApplication);
        await db.SaveChangesAsync();

        var handler = new GetApplicationsByStatusHandler(db, new FakePositionProfileReader());
        var result = await handler.HandleAsync(
            new GetApplicationsByStatusRequest(companyId, stages.ApplicationReceived.Id),
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(application.Id, item.ApplicationId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Applications_Match_Stage()
    {
        await using var db = BuildContext();
        var handler = new GetApplicationsByStatusHandler(db, new FakePositionProfileReader());

        var result = await handler.HandleAsync(
            new GetApplicationsByStatusRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
