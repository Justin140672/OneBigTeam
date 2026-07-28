using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetStaleVacancies;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetStaleVacanciesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Excludes_Vacancy_With_Recent_Application_Activity()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = OpenVacancy(companyId, Now.AddDays(-30));
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", "emma.clarke@example.com", null, null, Now);
        // Application activity 5 days ago — well within the default 14-day window.
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now.AddDays(-5));

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var handler = new GetStaleVacanciesHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetStaleVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_Vacancy_With_Old_Application_Activity()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = OpenVacancy(companyId, Now.AddDays(-60));
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Liam", "Turner", "liam.turner@example.com", null, null, Now);
        var lastActivity = Now.AddDays(-20);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, lastActivity);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var handler = new GetStaleVacanciesHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetStaleVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(vacancy.Id, item.VacancyId);
        Assert.Equal(lastActivity, item.LastActivityAt);
        Assert.Equal(20, item.DaysSinceActivity);
    }

    [Fact]
    public async Task HandleAsync_Includes_Open_Vacancy_With_No_Applications_Falling_Back_To_OpenedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = OpenVacancy(companyId, Now.AddDays(-20));

        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var handler = new GetStaleVacanciesHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetStaleVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(vacancy.Id, item.VacancyId);
        Assert.Null(item.LastActivityAt);
        Assert.Equal(20, item.DaysSinceActivity);
    }

    [Fact]
    public async Task HandleAsync_Excludes_NonOpen_Vacancy_Even_When_Activity_Is_Old()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        // Never opened — stays in Draft status.
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid(), Now.AddDays(-60));
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Noah", "Patel", "noah.patel@example.com", null, null, Now);
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now.AddDays(-40));

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var handler = new GetStaleVacanciesHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(new GetStaleVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Uses_Default_StaleAfterDays_Of_14_When_Not_Supplied()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = OpenVacancy(companyId, Now.AddDays(-60));
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Ava", "Bell", "ava.bell@example.com", null, null, Now);
        // 20 days since activity — stale under the default 14-day threshold.
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now.AddDays(-20));

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var handler = new GetStaleVacanciesHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(
            new GetStaleVacanciesRequest { CompanyId = companyId, StaleAfterDays = null },
            CancellationToken.None);

        Assert.Single(result.Items);
    }

    [Fact]
    public async Task HandleAsync_Honors_Custom_StaleAfterDays_Over_Default()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = OpenVacancy(companyId, Now.AddDays(-60));
        var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Sophie", "Wright", "sophie.wright@example.com", null, null, Now);
        // 20 days since activity — stale under the default 14 days, but not under a 30-day threshold.
        var application = Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now.AddDays(-20));

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        await db.SaveChangesAsync();

        var handler = new GetStaleVacanciesHandler(db, new FakeClock(FixedUtcNow), new FakePositionProfileReader());
        var result = await handler.HandleAsync(
            new GetStaleVacanciesRequest { CompanyId = companyId, StaleAfterDays = 30 },
            CancellationToken.None);

        Assert.Empty(result.Items);
    }

    private static Vacancy OpenVacancy(Guid companyId, DateTimeOffset openedAt)
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), openedAt);
        vacancy.Open(openedAt, DateOnly.FromDateTime(openedAt.Date));
        return vacancy;
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
