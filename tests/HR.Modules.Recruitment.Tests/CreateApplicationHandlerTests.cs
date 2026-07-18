using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateApplication;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class CreateApplicationHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Creates_Application_In_Applied_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ApplicationStatus.Applied, result.Value!.Status);
        Assert.Equal(vacancy.Id, result.Value.VacancyId);
        Assert.Equal(candidate.Id, result.Value.CandidateId);

        var saved = await db.Applications.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = Guid.NewGuid(), CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Candidate_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Candidate_Already_Applied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        await db.SaveChangesAsync();

        await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        var result = await handler(db).HandleAsync(
            new CreateApplicationRequest { CompanyId = companyId, VacancyId = vacancy.Id, CandidateId = candidate.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    private static CreateApplicationHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
