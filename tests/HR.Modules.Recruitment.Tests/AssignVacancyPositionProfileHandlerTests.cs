using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.AssignVacancyPositionProfile;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class AssignVacancyPositionProfileHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Assigns_PositionProfile_And_Persists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var originalPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, originalPositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var positionProfileId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new AssignVacancyPositionProfileRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                PositionProfileId = positionProfileId,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionProfileId, result.Value!.PositionProfileId);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.UpdatedAt);

        var saved = await db.Vacancies.SingleAsync(v => v.Id == vacancy.Id);
        Assert.Equal(positionProfileId, saved.PositionProfileId);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<VacancyPositionProfileAssignedAuditEvent>(published);
        Assert.Equal("vacancy.position_profile_assigned", ((IAuditEvent)auditEvent).EventType);
        Assert.Equal(vacancy.Id, ((IAuditEvent)auditEvent).EntityId);
        Assert.Equal(originalPositionProfileId, auditEvent.PreviousPositionProfileId);
        Assert.Equal(positionProfileId, auditEvent.PositionProfileId);
        Assert.Equal("manual", auditEvent.AssignmentMethod);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new AssignVacancyPositionProfileRequest
            {
                CompanyId = Guid.NewGuid(),
                VacancyId = Guid.NewGuid(),
                PositionProfileId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var otherCompanyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new AssignVacancyPositionProfileRequest
            {
                CompanyId = Guid.NewGuid(), // different company than the vacancy
                VacancyId = vacancy.Id,
                PositionProfileId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var originalPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, originalPositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, new FakePositionProfileReader(exists: false), auditPublisher).HandleAsync(
            new AssignVacancyPositionProfileRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                PositionProfileId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);

        var saved = await db.Vacancies.SingleAsync(v => v.Id == vacancy.Id);
        Assert.Equal(originalPositionProfileId, saved.PositionProfileId); // unchanged
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_PositionProfile_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var positionProfileId = Guid.NewGuid();
        var reader = new FakePositionProfileReader(
            matchingCompanyId: Guid.NewGuid(), // a different company than the request below
            matchingPositionProfileId: positionProfileId);
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, reader, auditPublisher).HandleAsync(
            new AssignVacancyPositionProfileRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                PositionProfileId = positionProfileId,
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Can_ReAssign_Vacancy_That_Already_Had_A_PositionProfileId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var originalPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, originalPositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var newPositionProfileId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher: auditPublisher).HandleAsync(
            new AssignVacancyPositionProfileRequest
            {
                CompanyId = companyId,
                VacancyId = vacancy.Id,
                PositionProfileId = newPositionProfileId,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(newPositionProfileId, result.Value!.PositionProfileId);

        var saved = await db.Vacancies.SingleAsync(v => v.Id == vacancy.Id);
        Assert.Equal(newPositionProfileId, saved.PositionProfileId);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<VacancyPositionProfileAssignedAuditEvent>(published);
        Assert.Equal(originalPositionProfileId, auditEvent.PreviousPositionProfileId);
        Assert.Equal(newPositionProfileId, auditEvent.PositionProfileId);
    }

    private static AssignVacancyPositionProfileHandler handler(
        RecruitmentDbContext db,
        HR.Infrastructure.Abstractions.IPositionProfileReader? positionProfileReader = null,
        FakeAuditPublisher? auditPublisher = null) =>
        new(db, new FakeClock(FixedUtcNow), positionProfileReader ?? new FakePositionProfileReader(), auditPublisher ?? new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
