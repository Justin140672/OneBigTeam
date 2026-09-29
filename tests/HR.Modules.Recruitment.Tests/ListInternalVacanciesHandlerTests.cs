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

    // ----- Internal recruitment Ticket 4: HasApplied -----

    private static Application ApplicationFor(Guid companyId, Vacancy vacancy, Candidate candidate, ApplicationSource? source = ApplicationSource.Internal) =>
        Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, Guid.NewGuid(), null, Now, source);

    [Fact]
    public async Task HandleAsync_HasApplied_Is_True_Only_For_Vacancies_The_Employees_Linked_Candidate_Applied_To()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var applied = OpenAdvertised(companyId, Guid.NewGuid(), "Applied Role");
        var appliedAndWithdrawn = OpenAdvertised(companyId, Guid.NewGuid(), "Withdrawn Role");
        var notApplied = OpenAdvertised(companyId, Guid.NewGuid(), "Other Role");
        var linked = Candidate.CreateForEmployee(Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", "priya.shah@acme.example", null, Now);
        var withdrawn = ApplicationFor(companyId, appliedAndWithdrawn, linked);
        withdrawn.Withdraw(Now);

        db.Vacancies.AddRange(applied, appliedAndWithdrawn, notApplied);
        db.Candidates.Add(linked);
        db.Applications.AddRange(ApplicationFor(companyId, applied, linked), withdrawn);
        await db.SaveChangesAsync();

        var result = await new ListInternalVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, employeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var items = result.Value!.Items.ToDictionary(i => i.Id);
        Assert.Equal(3, items.Count);
        Assert.True(items[applied.Id].HasApplied);
        Assert.True(items[appliedAndWithdrawn.Id].HasApplied);
        Assert.False(items[notApplied.Id].HasApplied);
    }

    [Fact]
    public async Task HandleAsync_HasApplied_Counts_Recruiter_Entered_Application_Of_Hired_Linked_Candidate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var vacancy = OpenAdvertised(companyId, Guid.NewGuid(), "Role");
        var hired = Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Shah", "priya.personal@example.com", null, null, Now);
        hired.LinkToEmployee(employeeId, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(hired);
        db.Applications.Add(ApplicationFor(companyId, vacancy, hired, ApplicationSource.JobBoard));
        await db.SaveChangesAsync();

        var result = await new ListInternalVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, employeeId, CancellationToken.None);

        Assert.True(Assert.Single(result.Value!.Items).HasApplied);
    }

    [Fact]
    public async Task HandleAsync_HasApplied_Is_False_Everywhere_When_No_Current_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var vacancy = OpenAdvertised(companyId, Guid.NewGuid(), "Role");
        var linked = Candidate.CreateForEmployee(Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", "priya.shah@acme.example", null, Now);
        db.Vacancies.Add(vacancy);
        db.Candidates.Add(linked);
        db.Applications.Add(ApplicationFor(companyId, vacancy, linked));
        await db.SaveChangesAsync();

        var handler = new ListInternalVacanciesHandler(db, new FakePositionProfileReader());

        var withNull = await handler.HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, currentEmployeeId: null, CancellationToken.None);
        var legacyOverload = await handler.HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.False(Assert.Single(withNull.Value!.Items).HasApplied);
        Assert.False(Assert.Single(legacyOverload.Value!.Items).HasApplied);
    }

    [Fact]
    public async Task HandleAsync_HasApplied_Does_Not_Leak_Another_Employees_Or_Unlinked_Candidates_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var colleagueId = Guid.NewGuid();
        var colleagueVacancy = OpenAdvertised(companyId, Guid.NewGuid(), "Colleague Role");
        var externalVacancy = OpenAdvertised(companyId, Guid.NewGuid(), "External Role");
        var colleague = Candidate.CreateForEmployee(Guid.NewGuid(), companyId, colleagueId, "Tom", "Baker", "tom.baker@acme.example", null, Now);
        var external = Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Shah", "priya.shah@acme.example", null, null, Now);

        db.Vacancies.AddRange(colleagueVacancy, externalVacancy);
        db.Candidates.AddRange(colleague, external);
        db.Applications.AddRange(
            ApplicationFor(companyId, colleagueVacancy, colleague),
            ApplicationFor(companyId, externalVacancy, external, ApplicationSource.Direct));
        await db.SaveChangesAsync();

        var result = await new ListInternalVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, employeeId, CancellationToken.None);

        Assert.Equal(2, result.Value!.Items.Count);
        Assert.All(result.Value.Items, i => Assert.False(i.HasApplied));
    }

    [Fact]
    public async Task HandleAsync_HasApplied_Ignores_Same_Employee_Id_Linked_In_Another_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var vacancy = OpenAdvertised(companyId, Guid.NewGuid(), "Role");
        // Pathological cross-tenant data: a candidate in another company linked to the same id, with an
        // application pointing at this company's vacancy id. It must not count.
        var otherCompanyCandidate = Candidate.CreateForEmployee(Guid.NewGuid(), otherCompanyId, employeeId, "Priya", "Shah", "priya.shah@acme.example", null, Now);

        db.Vacancies.Add(vacancy);
        db.Candidates.Add(otherCompanyCandidate);
        db.Applications.Add(ApplicationFor(otherCompanyId, vacancy, otherCompanyCandidate));
        await db.SaveChangesAsync();

        var result = await new ListInternalVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListInternalVacanciesRequest { CompanyId = companyId }, employeeId, CancellationToken.None);

        Assert.False(Assert.Single(result.Value!.Items).HasApplied);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
