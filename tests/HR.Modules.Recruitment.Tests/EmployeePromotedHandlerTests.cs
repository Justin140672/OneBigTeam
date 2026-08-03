using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CloseVacancyOnEmployeePromoted;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class EmployeePromotedHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Closes_Open_Vacancy_Matching_New_PositionProfile()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Senior Engineer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = new EmployeePromotedHandler(db, new FakeClock(FixedUtcNow), auditPublisher);

        await handler.HandleAsync(
            new EmployeePromotedIntegrationEvent(
                companyId, Guid.NewGuid(), Guid.NewGuid(), positionProfileId, DateOnly.FromDateTime(FixedUtcNow)),
            CancellationToken.None);

        var reloaded = await db.Vacancies.SingleAsync(v => v.Id == vacancy.Id);
        Assert.Equal(VacancyStatus.Closed, reloaded.Status);
        Assert.Single(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Leaves_Vacancy_For_Different_PositionProfile_Untouched()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancyPositionProfileId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, vacancyPositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var auditPublisher = new FakeAuditPublisher();
        var handler = new EmployeePromotedHandler(db, new FakeClock(FixedUtcNow), auditPublisher);

        await handler.HandleAsync(
            new EmployeePromotedIntegrationEvent(
                companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(FixedUtcNow)),
            CancellationToken.None);

        var reloaded = await db.Vacancies.SingleAsync(v => v.Id == vacancy.Id);
        Assert.Equal(VacancyStatus.Open, reloaded.Status);
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task HandleAsync_Does_Nothing_When_No_Vacancy_Exists_For_PositionProfile()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();
        var handler = new EmployeePromotedHandler(db, new FakeClock(FixedUtcNow), auditPublisher);

        var exception = await Record.ExceptionAsync(() => handler.HandleAsync(
            new EmployeePromotedIntegrationEvent(
                companyId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(FixedUtcNow)),
            CancellationToken.None));

        Assert.Null(exception);
        Assert.Empty(auditPublisher.Published);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
