using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CloseVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class CloseVacancyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Closes_Open_Vacancy()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new CloseVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VacancyStatus.Closed, result.Value!.Status);
        Assert.Equal(DateOnly.FromDateTime(FixedUtcNow), result.Value.ClosedAt);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<VacancyClosedAuditEvent>(published);
        Assert.Equal("vacancy.closed", ((IAuditEvent)auditEvent).EventType);
        Assert.Equal("Vacancy", ((IAuditEvent)auditEvent).EntityType);
        Assert.Equal(vacancy.Id, ((IAuditEvent)auditEvent).EntityId);
        Assert.Equal(VacancyStatus.Open, auditEvent.PreviousStatus);
        Assert.Equal("Senior Software Engineer", auditEvent.EffectiveTitle);
    }

    [Fact]
    public async Task HandleAsync_Uses_Provided_ClosedAt_Date()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var closedAt = new DateOnly(2026, 8, 1);

        var result = await handler(db).HandleAsync(
            new CloseVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id, ClosedAt = closedAt },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(closedAt, result.Value!.ClosedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new CloseVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Already_Closed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        vacancy.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new CloseVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Cancelled()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "HR Business Partner", null, Guid.NewGuid(), Now);
        vacancy.Cancel(Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new CloseVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_EffectiveTitle_Resolves_To_PositionProfile_Title_When_AdvertTitle_Is_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, null, null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Position Profile Title", null, null, true, null, null),
        };
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new CloseVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<VacancyClosedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("Position Profile Title", auditEvent.EffectiveTitle);
    }

    [Fact]
    public async Task HandleAsync_EffectiveTitle_Falls_Back_To_Untitled_When_No_PositionProfile_And_AdvertTitle_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), null, null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        // No summaries dictionary supplied — simulates the linked profile no longer being resolvable.
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new CloseVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var auditEvent = Assert.IsType<VacancyClosedAuditEvent>(Assert.Single(auditPublisher.Published));
        Assert.Equal("(untitled)", auditEvent.EffectiveTitle);
    }

    private static CloseVacancyHandler handler(
        RecruitmentDbContext db,
        FakeAuditPublisher? auditPublisher = null,
        IPositionProfileReader? positionProfileReader = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher(), positionProfileReader ?? new FakePositionProfileReader());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
