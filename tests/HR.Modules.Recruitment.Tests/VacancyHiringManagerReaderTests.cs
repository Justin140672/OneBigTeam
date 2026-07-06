using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class VacancyHiringManagerReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetHiringManagerIdForInterviewAsync_Returns_HiringManagerId_For_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, hiringManagerId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var reader = new VacancyHiringManagerReader(db);

        var result = await reader.GetHiringManagerIdForInterviewAsync(companyId, interview.Id, CancellationToken.None);

        Assert.Equal(hiringManagerId, result);
    }

    [Fact]
    public async Task GetHiringManagerIdForInterviewAsync_Returns_Null_When_Interview_Missing()
    {
        await using var db = BuildContext();
        var reader = new VacancyHiringManagerReader(db);

        var result = await reader.GetHiringManagerIdForInterviewAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetHiringManagerIdForInterviewAsync_Returns_Null_When_Interview_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Backend Engineer", null, null, hiringManagerId, Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now);
        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), Now.AddDays(2), null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        await db.SaveChangesAsync();

        var reader = new VacancyHiringManagerReader(db);

        var result = await reader.GetHiringManagerIdForInterviewAsync(otherCompanyId, interview.Id, CancellationToken.None);

        Assert.Null(result);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
