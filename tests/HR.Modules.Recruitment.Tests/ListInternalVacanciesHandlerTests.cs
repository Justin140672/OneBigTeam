using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListInternalVacancies;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class ListInternalVacanciesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 10, 0, 0, TimeSpan.Zero);

    private static Vacancy OpenAdvertised(Guid companyId, Guid positionProfileId, string? advertTitle)
    {
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, advertTitle, null, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        return vacancy;
    }

    [Fact]
    public async Task HandleAsync_Includes_Only_Open_And_Advertised_Vacancies_For_The_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var included = OpenAdvertised(companyId, Guid.NewGuid(), "Included Role");

        var draftAdvertised = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Draft Advertised", null, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true);

        var onHoldAdvertised = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "OnHold Advertised", null, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true);
        onHoldAdvertised.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        onHoldAdvertised.Hold(Now);

        var closedAdvertised = OpenAdvertised(companyId, Guid.NewGuid(), "Closed Advertised");
        closedAdvertised.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));

        var cancelledAdvertised = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Cancelled Advertised", null, Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: true);
        cancelledAdvertised.Cancel(Now);

        var openNotAdvertised = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Open Not Advertised", null, Guid.NewGuid(), Now);
        openNotAdvertised.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));

        var otherCompany = OpenAdvertised(otherCompanyId, Guid.NewGuid(), "Other Company Role");

        db.Vacancies.AddRange(included, draftAdvertised, onHoldAdvertised, closedAdvertised, cancelledAdvertised, openNotAdvertised, otherCompany);
        await db.SaveChangesAsync();

        var result = await new ListInternalVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(included.Id, item.Id);
        Assert.Equal("Included Role", item.Title);
    }

    [Fact]
    public async Task HandleAsync_Title_Falls_Back_AdvertTitle_Then_PositionProfileTitle_Then_Untitled()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ppWithTitle = Guid.NewGuid();
        var ppUnresolved = Guid.NewGuid();

        var withAdvertTitle = OpenAdvertised(companyId, ppWithTitle, "Advert Title Wins");
        var fromProfileTitle = OpenAdvertised(companyId, ppWithTitle, null);
        var untitled = OpenAdvertised(companyId, ppUnresolved, null);
        db.Vacancies.AddRange(withAdvertTitle, fromProfileTitle, untitled);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [ppWithTitle] = new(ppWithTitle, "Profile Title", null, null, true, null, "Head Office", "Engineering"),
        };

        var result = await new ListInternalVacanciesHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var titles = result.Value!.Items.Select(i => i.Title).ToList();
        Assert.Contains("Advert Title Wins", titles);
        Assert.Contains("Profile Title", titles);
        Assert.Contains("(untitled)", titles);
    }

    [Fact]
    public async Task HandleAsync_Resolves_DepartmentName_And_Location_From_PositionProfile_Summary()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var vacancy = OpenAdvertised(companyId, positionProfileId, "Role");
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Role", null, null, true, Guid.NewGuid(), "Bristol Office", "Finance"),
        };

        var result = await new ListInternalVacanciesHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Finance", item.DepartmentName);
        Assert.Equal("Bristol Office", item.Location);
    }

    [Fact]
    public async Task HandleAsync_Search_Filters_On_Title_Or_Department_CaseInsensitively()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ppEng = Guid.NewGuid();
        var ppFin = Guid.NewGuid();

        var engineer = OpenAdvertised(companyId, ppEng, "Backend Engineer");
        var accountant = OpenAdvertised(companyId, ppFin, "Accountant");
        db.Vacancies.AddRange(engineer, accountant);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [ppEng] = new(ppEng, "Backend Engineer", null, null, true, null, null, "Engineering"),
            [ppFin] = new(ppFin, "Accountant", null, null, true, null, null, "Finance"),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);

        var byTitle = await new ListInternalVacanciesHandler(db, reader).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId, Search = "backend" }, CancellationToken.None);
        Assert.Equal("Backend Engineer", Assert.Single(byTitle.Value!.Items).Title);

        var byDepartment = await new ListInternalVacanciesHandler(db, reader).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId, Search = "FINANCE" }, CancellationToken.None);
        Assert.Equal("Accountant", Assert.Single(byDepartment.Value!.Items).Title);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_Title()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.Vacancies.AddRange(
            OpenAdvertised(companyId, Guid.NewGuid(), "Zebra Handler"),
            OpenAdvertised(companyId, Guid.NewGuid(), "Alpha Analyst"),
            OpenAdvertised(companyId, Guid.NewGuid(), "Mango Manager"));
        await db.SaveChangesAsync();

        var result = await new ListInternalVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "Alpha Analyst", "Mango Manager", "Zebra Handler" }, result.Value!.Items.Select(i => i.Title));
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
