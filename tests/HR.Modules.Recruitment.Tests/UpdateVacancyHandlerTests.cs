using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.UpdateVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class UpdateVacancyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Updates_Vacancy_Details()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Old Title", null, null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var newHiringManagerId = Guid.NewGuid();
        var newDepartmentId = Guid.NewGuid();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId       = companyId,
                VacancyId       = vacancy.Id,
                DepartmentId    = newDepartmentId,
                Title           = "New Title",
                Description     = "Updated description",
                Location        = "Remote",
                HiringManagerId = newHiringManagerId,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
        Assert.Equal("Updated description", result.Value.Description);
        Assert.Equal("Remote", result.Value.Location);
        Assert.Equal(newDepartmentId, result.Value.DepartmentId);
        Assert.Equal(newHiringManagerId, result.Value.HiringManagerId);

        var published = Assert.Single(auditPublisher.Published);
        var auditEvent = Assert.IsType<VacancyUpdatedAuditEvent>(published);
        Assert.Equal("vacancy.updated", ((IAuditEvent)auditEvent).EventType);
        Assert.Equal("Vacancy", ((IAuditEvent)auditEvent).EntityType);
        Assert.Equal(vacancy.Id, ((IAuditEvent)auditEvent).EntityId);
        Assert.Equal("Old Title", auditEvent.Before.Title);
        Assert.Equal("New Title", auditEvent.After.Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();
        var auditPublisher = new FakeAuditPublisher();

        var result = await handler(db, auditPublisher).HandleAsync(
            new UpdateVacancyRequest
            {
                CompanyId       = Guid.NewGuid(),
                VacancyId       = Guid.NewGuid(),
                Title           = "Title",
                HiringManagerId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(auditPublisher.Published);
    }

    private static UpdateVacancyHandler handler(RecruitmentDbContext db, FakeAuditPublisher? auditPublisher = null) =>
        new(db, new FakeClock(FixedUtcNow), auditPublisher ?? new FakeAuditPublisher());

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
