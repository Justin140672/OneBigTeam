using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListApplicationsForVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ListApplicationsForVacancyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Applications_For_Vacancy_With_Candidate_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Emma", result.Value.Items[0].CandidateFirstName);
        Assert.Equal(stages.ApplicationReceived.Id, result.Value.Items[0].CurrentStageId);
        Assert.False(result.Value.Items[0].IsWithdrawn);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_StageId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidateA = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var candidateB = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);

        var applied = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateA.Id, stages.ApplicationReceived.Id, null, Now);
        var cvReview = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateB.Id, stages.CvReview.Id, null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidateA, candidateB);
        db.Applications.AddRange(applied, cvReview);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id, StageId = stages.CvReview.Id },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Liam", result.Value.Items[0].CandidateFirstName);
    }

    [Fact]
    public async Task HandleAsync_Flags_Withdrawn_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);
        application.Withdraw(Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.Value!.Items[0].IsWithdrawn);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Applications_For_Other_Vacancies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyA = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        var vacancyB = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyA.Id, candidate.Id, stages.ApplicationReceived.Id, null, Now);

        db.Vacancies.AddRange(vacancyA, vacancyB);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancyB.Id },
            CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
