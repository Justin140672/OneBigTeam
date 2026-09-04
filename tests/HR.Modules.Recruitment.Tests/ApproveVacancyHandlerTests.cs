using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ApproveVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ApproveVacancyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task HandleAsync_Approves_Draft_Vacancy() => HandleAsync_Approves_Vacancy_For_Status(VacancyStatus.Draft);

    [Fact]
    public Task HandleAsync_Approves_OnHold_Vacancy() => HandleAsync_Approves_Vacancy_For_Status(VacancyStatus.OnHold);

    [Fact]
    public Task HandleAsync_Approves_Open_Vacancy() => HandleAsync_Approves_Vacancy_For_Status(VacancyStatus.Open);

    // Not a [Theory] — VacancyStatus is internal to HR.Modules.Recruitment and cannot be used as a
    // public Theory parameter (see the identical note in GetExternalRecruiterUsageHandlerTests /
    // GetRecruitmentStageUsageHandlerTests). Each status is exercised via its own [Fact] above.
    private async Task HandleAsync_Approves_Vacancy_For_Status(VacancyStatus status)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var openedAt = DateOnly.FromDateTime(Now.UtcDateTime);
        if (status is VacancyStatus.Open or VacancyStatus.OnHold)
            vacancy.Open(Now, openedAt);
        if (status is VacancyStatus.OnHold)
            vacancy.Hold(Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var approvedBy = Guid.NewGuid();

        var result = await handler(db, auditPublisher).HandleAsync(
            new ApproveVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            approvedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(approvedBy, result.Value!.ApprovedByUserId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(approvedBy, saved.ApprovedByUserId);
        Assert.NotNull(saved.ApprovedAt);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<VacancyApprovedAuditEvent>(published);
        Assert.Equal(approvedBy, auditEvent.ApprovedByUserId);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new ApproveVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid() },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Vacancy_Is_Closed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        var date = DateOnly.FromDateTime(Now.UtcDateTime);
        vacancy.Open(Now, date);
        vacancy.Close(Now, date);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new ApproveVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Vacancy_Is_Cancelled()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        vacancy.Cancel(Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new ApproveVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    private static ApproveVacancyHandler handler(
        RecruitmentDbContext db,
        FakeAuditPublisher? auditPublisher = null,
        IPositionProfileReader? positionProfileReader = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher(), positionProfileReader ?? new FakePositionProfileReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
