using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ApplyPositionProfileMatches;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// LEGACY / dead-in-practice: <see cref="Vacancy.PositionProfileId"/> is now a non-nullable
/// <see cref="Guid"/> (see the comment on that property), so there is no compiler-representable way
/// to construct a vacancy that "needs" an auto-matched position profile any more — every vacancy
/// always has one. ApplyPositionProfileMatchesHandler was rewritten to always short-circuit and
/// return an empty result; these tests assert exactly that, regardless of what vacancy data exists in
/// the database, and confirm it never touches existing rows.
/// </summary>
public class ApplyPositionProfileMatchesHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Always_Returns_Empty_Result_When_No_Vacancies_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, activeMatches: [Guid.NewGuid()], auditPublisher: auditPublisher).HandleAsync(
            new ApplyPositionProfileMatchesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Results);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Never_Touches_Vacancy_That_Already_Has_PositionProfileId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var existingPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, existingPositionProfileId, "Frontend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db, activeMatches: [Guid.NewGuid()]).HandleAsync(
            new ApplyPositionProfileMatchesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Results);

        var saved = await db.Vacancies.SingleAsync(v => v.Id == vacancy.Id);
        Assert.Equal(existingPositionProfileId, saved.PositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Is_Scoped_To_Company_And_Does_Not_Touch_Other_Companies_Vacancies()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherCompanyPositionProfileId = Guid.NewGuid();
        var otherCompanyVacancy = Vacancy.Create(
            Guid.NewGuid(), otherCompanyId, otherCompanyPositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(otherCompanyVacancy);
        await db.SaveChangesAsync();

        var result = await handler(db, activeMatches: [Guid.NewGuid()]).HandleAsync(
            new ApplyPositionProfileMatchesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Results);

        var saved = await db.Vacancies.SingleAsync(v => v.Id == otherCompanyVacancy.Id);
        Assert.Equal(otherCompanyPositionProfileId, saved.PositionProfileId);
    }

    private static ApplyPositionProfileMatchesHandler handler(
        RecruitmentDbContext db,
        IReadOnlyList<Guid>? activeMatches = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(
            db,
            new VacancyPositionProfileMatcher(new FakePositionProfileReader(activeMatches: activeMatches ?? [])),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
