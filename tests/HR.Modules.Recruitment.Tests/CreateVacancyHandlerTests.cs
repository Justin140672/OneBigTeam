using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.CreateVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class CreateVacancyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_Vacancy_In_Draft_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId       = companyId,
                Title           = "Senior Software Engineer",
                HiringManagerId = hiringManagerId,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId, result.Value!.CompanyId);
        Assert.Equal("Senior Software Engineer", result.Value.Title);
        Assert.Equal(VacancyStatus.Draft, result.Value.Status);
        Assert.Equal(hiringManagerId, result.Value.HiringManagerId);

        var saved = await db.Vacancies.SingleAsync();
        Assert.Equal(result.Value.Id, saved.Id);
    }

    [Fact]
    public async Task HandleAsync_Trims_Title_Description_And_Location()
    {
        await using var db = BuildContext();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId       = Guid.NewGuid(),
                Title           = "  Backend Engineer  ",
                Description     = "  Own the payments platform  ",
                Location        = "  Remote  ",
                HiringManagerId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Backend Engineer", result.Value!.Title);
        Assert.Equal("Own the payments platform", result.Value.Description);
        Assert.Equal("Remote", result.Value.Location);
    }

    [Fact]
    public async Task HandleAsync_Assigns_Optional_DepartmentId()
    {
        await using var db = BuildContext();
        var departmentId = Guid.NewGuid();

        var result = await handler(db).HandleAsync(
            new CreateVacancyRequest
            {
                CompanyId       = Guid.NewGuid(),
                DepartmentId    = departmentId,
                Title           = "Product Designer",
                HiringManagerId = Guid.NewGuid(),
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(departmentId, result.Value!.DepartmentId);
    }

    private static CreateVacancyHandler handler(RecruitmentDbContext db) =>
        new(db, new FakeClock(FixedUtcNow));

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
