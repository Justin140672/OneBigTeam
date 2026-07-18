using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.ListVacancies;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
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
            Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now),
            Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
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

        var open = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Open Role", null, Guid.NewGuid(), Now);
        open.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));

        db.Vacancies.AddRange(
            open,
            Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Draft Role", null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId, Status = VacancyStatus.Open },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Open Role", result.Value.Items[0].AdvertTitle);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Vacancies_From_Other_Companies()
    {
        await using var db = BuildContext();

        db.Vacancies.AddRange(
            Vacancy.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Role A", null, Guid.NewGuid(), Now),
            Vacancy.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Role B", null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListVacanciesRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Resolves_PositionProfile_Title_And_DepartmentId_For_Each_Item()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var sharedProfileId = Guid.NewGuid();
        var otherProfileId = Guid.NewGuid();
        var sharedProfileDepartmentId = Guid.NewGuid();
        var otherProfileDepartmentId = Guid.NewGuid();

        var vacancyA = Vacancy.Create(Guid.NewGuid(), companyId, sharedProfileId, "Backend Engineer A", null, Guid.NewGuid(), Now);
        var vacancyB = Vacancy.Create(Guid.NewGuid(), companyId, sharedProfileId, "Backend Engineer B", null, Guid.NewGuid(), Now);
        var vacancyC = Vacancy.Create(Guid.NewGuid(), companyId, otherProfileId, "Product Designer", null, Guid.NewGuid(), Now);

        db.Vacancies.AddRange(vacancyA, vacancyB, vacancyC);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [sharedProfileId] = new(sharedProfileId, "Shared Profile Title", sharedProfileDepartmentId, "Shared description", true, null, null),
            [otherProfileId] = new(otherProfileId, "Other Profile Title", otherProfileDepartmentId, "Other description", true, null, null),
        };

        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);

        var itemA = result.Value.Items.Single(i => i.Id == vacancyA.Id);
        Assert.Equal("Shared Profile Title", itemA.PositionProfileTitle);
        Assert.Equal(sharedProfileDepartmentId, itemA.PositionProfileDepartmentId);

        var itemB = result.Value.Items.Single(i => i.Id == vacancyB.Id);
        Assert.Equal("Shared Profile Title", itemB.PositionProfileTitle);
        Assert.Equal(sharedProfileDepartmentId, itemB.PositionProfileDepartmentId);

        var itemC = result.Value.Items.Single(i => i.Id == vacancyC.Id);
        Assert.Equal("Other Profile Title", itemC.PositionProfileTitle);
        Assert.Equal(otherProfileDepartmentId, itemC.PositionProfileDepartmentId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_PositionProfile_Fields_When_Linked_Profile_Does_Not_Resolve()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        // No summaries dictionary supplied — simulates the linked profile no longer being resolvable.
        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.PositionProfileTitle);
        Assert.Null(item.PositionProfileDepartmentId);
    }

    [Fact]
    public async Task HandleAsync_Still_Resolves_PositionProfile_Title_And_DepartmentId_When_Linked_Profile_Is_Deactivated()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var profileDepartmentId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Deactivated Profile Title", profileDepartmentId, "Deactivated profile description", false, null, null),
        };

        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Deactivated Profile Title", item.PositionProfileTitle);
        Assert.Equal(profileDepartmentId, item.PositionProfileDepartmentId);
    }

    [Fact]
    public async Task HandleAsync_Resolves_EffectiveTitle_From_Vacancy_And_EffectiveLocation_Purely_From_PositionProfile_Per_Item()
    {
        // Location is no longer a vacancy-level concept — EffectiveLocation is resolved exclusively
        // from each item's linked Position Profile's PositionProfileSummary.LocationName, with no
        // vacancy-level override or fallback logic. EffectiveTitle, by contrast, still honours a
        // vacancy-level AdvertTitle override when set.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var profileWithAdvertOverrideId = Guid.NewGuid();
        var profileWithoutAdvertOverrideId = Guid.NewGuid();

        // Item A: has its own AdvertTitle override — should win over the Position Profile's title.
        var vacancyA = Vacancy.Create(
            Guid.NewGuid(), companyId, profileWithAdvertOverrideId,
            "Advert Title Override", null, Guid.NewGuid(), Now);

        // Item B: no AdvertTitle override — falls back to the Position Profile's title.
        var vacancyB = Vacancy.Create(
            Guid.NewGuid(), companyId, profileWithoutAdvertOverrideId,
            null, null, Guid.NewGuid(), Now);

        db.Vacancies.AddRange(vacancyA, vacancyB);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [profileWithAdvertOverrideId] = new(
                profileWithAdvertOverrideId, "Profile Title A", null, null, true, Guid.NewGuid(), "Profile Location A"),
            [profileWithoutAdvertOverrideId] = new(
                profileWithoutAdvertOverrideId, "Profile Title B", null, null, true, Guid.NewGuid(), "Profile Location B"),
        };

        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        var itemA = result.Value!.Items.Single(i => i.Id == vacancyA.Id);
        Assert.Equal("Advert Title Override", itemA.EffectiveTitle);
        Assert.Equal("Profile Location A", itemA.EffectiveLocation);

        var itemB = result.Value.Items.Single(i => i.Id == vacancyB.Id);
        Assert.Equal("Profile Title B", itemB.EffectiveTitle);
        Assert.Equal("Profile Location B", itemB.EffectiveLocation);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_PositionProfileId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var matchingProfileId = Guid.NewGuid();

        var matching = Vacancy.Create(Guid.NewGuid(), companyId, matchingProfileId, "Matching Role", null, Guid.NewGuid(), Now);
        var other = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Other Role", null, Guid.NewGuid(), Now);

        db.Vacancies.AddRange(matching, other);
        await db.SaveChangesAsync();

        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId, PositionProfileId = matchingProfileId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(matching.Id, item.Id);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_DepartmentId_Via_PositionProfileReader()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var profileInDepartmentId = Guid.NewGuid();
        var profileOutsideDepartmentId = Guid.NewGuid();

        var inDepartment = Vacancy.Create(Guid.NewGuid(), companyId, profileInDepartmentId, "In Department", null, Guid.NewGuid(), Now);
        var outsideDepartment = Vacancy.Create(Guid.NewGuid(), companyId, profileOutsideDepartmentId, "Outside Department", null, Guid.NewGuid(), Now);

        db.Vacancies.AddRange(inDepartment, outsideDepartment);
        await db.SaveChangesAsync();

        var reader = new FakePositionProfileReader(idsByDepartment: [profileInDepartmentId]);

        var result = await new ListVacanciesHandler(db, reader).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId, DepartmentId = departmentId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(inDepartment.Id, item.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_DepartmentId_Filter_Has_No_Matching_Profiles()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        db.Vacancies.Add(Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Some Role", null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        // No idsByDepartment supplied — the department has no matching position profiles.
        var result = await new ListVacanciesHandler(db, new FakePositionProfileReader()).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId, DepartmentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Combines_PositionProfileId_And_DepartmentId_Filters()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var matchingProfileId = Guid.NewGuid();
        var otherProfileInSameDepartmentId = Guid.NewGuid();

        var matching = Vacancy.Create(Guid.NewGuid(), companyId, matchingProfileId, "Matching Role", null, Guid.NewGuid(), Now);
        var sameDepartmentDifferentProfile = Vacancy.Create(
            Guid.NewGuid(), companyId, otherProfileInSameDepartmentId, "Same Department Different Profile", null, Guid.NewGuid(), Now);

        db.Vacancies.AddRange(matching, sameDepartmentDifferentProfile);
        await db.SaveChangesAsync();

        var reader = new FakePositionProfileReader(idsByDepartment: [matchingProfileId, otherProfileInSameDepartmentId]);

        var result = await new ListVacanciesHandler(db, reader).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId, DepartmentId = departmentId, PositionProfileId = matchingProfileId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(matching.Id, item.Id);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Other_Company_Vacancies_Even_When_Filters_Match()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var sharedProfileId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        var ownVacancy = Vacancy.Create(Guid.NewGuid(), companyId, sharedProfileId, "Own Company Role", null, Guid.NewGuid(), Now);
        var otherCompanyVacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, sharedProfileId, "Other Company Role", null, Guid.NewGuid(), Now);

        db.Vacancies.AddRange(ownVacancy, otherCompanyVacancy);
        await db.SaveChangesAsync();

        var reader = new FakePositionProfileReader(idsByDepartment: [sharedProfileId]);

        var result = await new ListVacanciesHandler(db, reader).HandleAsync(
            new ListVacanciesRequest { CompanyId = companyId, DepartmentId = departmentId, PositionProfileId = sharedProfileId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(ownVacancy.Id, item.Id);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
