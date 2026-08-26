using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.PublishVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class PublishVacancyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Publishes_Draft_Vacancy_Successfully()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new PublishVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VacancyStatus.Open, result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new PublishVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Vacancy_Already_Open()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db).HandleAsync(
            new PublishVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    // SET-05: VacancyApprovalRequired gating.

    [Fact]
    public async Task HandleAsync_Fails_When_VacancyApprovalRequired_And_Vacancy_Not_Approved()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var settingsReader = new FakeCompanyRecruitmentSettingsReader(
            new CompanyRecruitmentSettings(true, false, 730));

        var result = await handler(db, recruitmentSettingsReader: settingsReader).HandleAsync(
            new PublishVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("This vacancy requires approval before it can be published.", result.Error.Message);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(VacancyStatus.Draft, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_VacancyApprovalRequired_And_Vacancy_Approved()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        vacancy.Approve(Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var settingsReader = new FakeCompanyRecruitmentSettingsReader(
            new CompanyRecruitmentSettings(true, false, 730));

        var result = await handler(db, recruitmentSettingsReader: settingsReader).HandleAsync(
            new PublishVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VacancyStatus.Open, result.Value!.Status);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_VacancyApprovalRequired_Is_False_Regardless_Of_Approval_State()
    {
        // Regression coverage: default settings (VacancyApprovalRequired = false) must not change
        // pre-existing PublishVacancy behaviour for an unapproved vacancy.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await handler(db, recruitmentSettingsReader: new FakeCompanyRecruitmentSettingsReader()).HandleAsync(
            new PublishVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VacancyStatus.Open, result.Value!.Status);
    }

    private static PublishVacancyHandler handler(
        RecruitmentDbContext db,
        FakeAuditPublisher? auditPublisher = null,
        IPositionProfileReader? positionProfileReader = null,
        FakeCompanyRecruitmentSettingsReader? recruitmentSettingsReader = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher(),
            positionProfileReader ?? new FakePositionProfileReader(),
            recruitmentSettingsReader ?? new FakeCompanyRecruitmentSettingsReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
