using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListVacancies;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ListVacanciesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Vacancies_For_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.Vacancies.AddRange(
            Vacancy.Create(Guid.NewGuid(), companyId, null, "Senior Software Engineer", null, null, Guid.NewGuid(), Now),
            Vacancy.Create(Guid.NewGuid(), companyId, null, "Product Designer", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListVacanciesHandler(db).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var open = Vacancy.Create(Guid.NewGuid(), companyId, null, "Open Role", null, null, Guid.NewGuid(), Now);
        open.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));

        db.Vacancies.AddRange(
            open,
            Vacancy.Create(Guid.NewGuid(), companyId, null, "Draft Role", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListVacanciesHandler(db).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId, Status = VacancyStatus.Open },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Open Role", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_DepartmentId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var engineeringId = Guid.NewGuid();

        db.Vacancies.AddRange(
            Vacancy.Create(Guid.NewGuid(), companyId, engineeringId, "Backend Engineer", null, null, Guid.NewGuid(), Now),
            Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "HR Business Partner", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListVacanciesHandler(db).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId, DepartmentId = engineeringId },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Backend Engineer", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Vacancies_From_Other_Companies()
    {
        await using var db = BuildContext();

        db.Vacancies.AddRange(
            Vacancy.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Role A", null, null, Guid.NewGuid(), Now),
            Vacancy.Create(Guid.NewGuid(), Guid.NewGuid(), null, "Role B", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListVacanciesHandler(db).HandleAsync(
            new ListVacanciesRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
