using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListApplicationsForVacancy;
using HR.Modules.Recruitment.Persistence;
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
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
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
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
        var candidateA = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var candidateB = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);

        var applied = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateA.Id, null, Now);
        var screening = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidateB.Id, null, Now);
        screening.MoveToScreening(Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.AddRange(candidateA, candidateB);
        db.Applications.AddRange(applied, screening);
        await db.SaveChangesAsync();

        var result = await new ListApplicationsForVacancyHandler(db).HandleAsync(
            new ListApplicationsForVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id, Status = ApplicationStatus.Screening },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Liam", result.Value.Items[0].CandidateFirstName);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Applications_For_Other_Vacancies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyA = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
        var vacancyB = Vacancy.Create(Guid.NewGuid(), companyId, null, "Product Designer", null, null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancyA.Id, candidate.Id, null, Now);

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
