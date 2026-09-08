using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetInternalVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class GetInternalVacancyHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static Vacancy OpenAdvertised(Guid companyId, Guid positionProfileId, string? advertTitle, string? advertDescription = null)
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, advertTitle, advertDescription, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        return vacancy;
    }

    [Fact]
    public async Task HandleAsync_Returns_Open_Advertised_Vacancy_With_Description_From_AdvertDescription()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = OpenAdvertised(companyId, positionProfileId, "Role Title", "Advert description");
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Profile Title", null, "Profile description", true, null, "HQ", "Ops"),
        };

        var result = await new GetInternalVacancyHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new GetInternalVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(vacancy.Id, result.Value!.Id);
        Assert.Equal("Role Title", result.Value.Title);
        Assert.Equal("Advert description", result.Value.Description);
        Assert.Equal("Ops", result.Value.DepartmentName);
        Assert.Equal("HQ", result.Value.Location);
        Assert.Equal(vacancy.OpenedAt, result.Value.OpenedAt);
    }

    [Fact]
    public async Task HandleAsync_Description_Falls_Back_To_PositionProfile_Description()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = OpenAdvertised(companyId, positionProfileId, "Role Title", advertDescription: null);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Profile Title", null, "Profile description", true, null, null, null),
        };

        var result = await new GetInternalVacancyHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new GetInternalVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Profile description", result.Value!.Description);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("closed")]
    [InlineData("not-advertised")]
    public async Task HandleAsync_Returns_NotFound_For_Non_Visible_Vacancy(string scenario)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        Vacancy vacancy = scenario switch
        {
            "draft" => Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Role", null, Guid.NewGuid(), Now,
                assignedRecruiterId: null, isAdvertisedInternally: true),
            "closed" => BuildClosed(companyId),
            _ => BuildOpenNotAdvertised(companyId),
        };
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetInternalVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetInternalVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static Vacancy BuildClosed(Guid companyId)
    {
        var v = OpenAdvertised(companyId, Guid.NewGuid(), "Role");
        v.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        return v;
    }

    private static Vacancy BuildOpenNotAdvertised(Guid companyId)
    {
        var v = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Role", null, Guid.NewGuid(), Now);
        v.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        return v;
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Cross_Company_Vacancy()
    {
        await using var db = BuildContext();
        var vacancy = OpenAdvertised(Guid.NewGuid(), Guid.NewGuid(), "Role");
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetInternalVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetInternalVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = vacancy.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Missing_Id()
    {
        await using var db = BuildContext();

        var result = await new GetInternalVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetInternalVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
