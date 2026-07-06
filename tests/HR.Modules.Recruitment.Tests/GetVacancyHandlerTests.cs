using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetVacancy;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetVacancyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Vacancy_When_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(vacancy.Id, result.Value!.Id);
        Assert.Equal("Senior Software Engineer", result.Value.Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();

        var result = await new GetVacancyHandler(db).HandleAsync(
            new GetVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var vacancy = Vacancy.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Backend Engineer", null, null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db).HandleAsync(
            new GetVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
