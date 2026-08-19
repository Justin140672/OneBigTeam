using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetVacancy;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Tests.Infrastructure;
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

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Senior Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(vacancy.Id, result.Value!.Id);
        Assert.Equal("Senior Software Engineer", result.Value.AdvertTitle);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        await using var db = BuildContext();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var vacancy = Vacancy.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = Guid.NewGuid(), VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_PositionProfile_Fields_When_Linked_Profile_Resolves()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Senior Software Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Some Title", departmentId, "Some description", true, null, null),
        };

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Some Title", result.Value!.PositionProfileTitle);
        Assert.Equal(departmentId, result.Value.PositionProfileDepartmentId);
        Assert.Equal("Some description", result.Value.PositionProfileDescription);
        Assert.True(result.Value.PositionProfileIsActive);
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
        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.PositionProfileTitle);
        Assert.Null(result.Value.PositionProfileDepartmentId);
        Assert.Null(result.Value.PositionProfileDescription);
        Assert.Null(result.Value.PositionProfileIsActive);
    }

    [Fact]
    public async Task HandleAsync_Returns_PositionProfileIsActive_False_When_Linked_Profile_Is_Deactivated()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var profileDepartmentId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", "Vacancy's own description", Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Deactivated Profile Title", profileDepartmentId, "Deactivated profile description", false, null, null),
        };

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The now-inactive linked profile's fields are still surfaced...
        Assert.Equal("Deactivated Profile Title", result.Value!.PositionProfileTitle);
        Assert.Equal(profileDepartmentId, result.Value.PositionProfileDepartmentId);
        Assert.Equal("Deactivated profile description", result.Value.PositionProfileDescription);
        Assert.False(result.Value.PositionProfileIsActive);
        // ...while the vacancy's own AdvertTitle/AdvertDescription remain untouched by this story.
        Assert.Equal("Backend Engineer", result.Value.AdvertTitle);
        Assert.Equal("Vacancy's own description", result.Value.AdvertDescription);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_PositionProfile_Fields_When_Linked_PositionProfile_Cannot_Be_Found()
    {
        // PositionProfileId is always populated on a Vacancy (non-nullable Guid) — the only way the
        // PositionProfile* fields can be null is when IPositionProfileReader can no longer resolve a
        // summary for that ID (e.g. the linked profile was deleted out from under the vacancy).
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Legacy Role", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(positionProfileId, result.Value!.PositionProfileId);
        Assert.Null(result.Value.PositionProfileTitle);
        Assert.Null(result.Value.PositionProfileDepartmentId);
        Assert.Null(result.Value.PositionProfileDescription);
        Assert.Null(result.Value.PositionProfileIsActive);
    }

    [Fact]
    public async Task HandleAsync_EffectiveTitle_Equals_AdvertTitle_When_Set_Even_If_PositionProfile_Title_Differs()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Vacancy Advert Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Position Profile Title", null, null, true, null, null),
        };

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Vacancy Advert Title", result.Value!.EffectiveTitle);
    }

    [Fact]
    public async Task HandleAsync_EffectiveTitle_Equals_PositionProfileTitle_When_AdvertTitle_Is_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, null, null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Position Profile Title", null, null, true, null, null),
        };

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Position Profile Title", result.Value!.EffectiveTitle);
    }

    [Fact]
    public async Task HandleAsync_EffectiveTitle_Falls_Back_To_Untitled_When_Neither_AdvertTitle_Nor_PositionProfile_Resolve()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), null, null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("(untitled)", result.Value!.EffectiveTitle);
    }

    [Fact]
    public async Task HandleAsync_EffectiveLocation_Is_Resolved_Purely_From_PositionProfile()
    {
        // Location is no longer a vacancy-level concept at all — EffectiveLocation is resolved
        // exclusively from the linked Position Profile's PositionProfileSummary.LocationName, with
        // no vacancy-level override or fallback logic.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Title", null, null, true, Guid.NewGuid(), "Position Profile Location"),
        };

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Position Profile Location", result.Value!.EffectiveLocation);
    }

    [Fact]
    public async Task HandleAsync_EffectiveLocation_Is_Null_When_PositionProfile_Has_No_Location()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Title", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Title", null, null, true, null, null),
        };

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader(summaries: summaries)).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.EffectiveLocation);
    }

    [Fact]
    public async Task HandleAsync_ApplicationCount_Reflects_Number_Of_Linked_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now));
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now));
        // An application linked to a different vacancy should not be counted.
        var otherVacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Other Role", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(otherVacancy);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, otherVacancy.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now));
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.ApplicationCount);
    }

    [Fact]
    public async Task HandleAsync_CanChangePositionProfile_Is_True_When_Draft_And_No_Applications()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.ApplicationCount);
        Assert.True(result.Value.CanChangePositionProfile);
    }

    [Fact]
    public async Task HandleAsync_CanChangePositionProfile_Is_False_When_Vacancy_Has_An_Application()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        db.Vacancies.Add(vacancy);
        db.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, Guid.NewGuid(), Guid.NewGuid(), null, Now));
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.ApplicationCount);
        Assert.False(result.Value.CanChangePositionProfile);
    }

    [Fact]
    public async Task HandleAsync_CanChangePositionProfile_Is_False_When_Vacancy_Is_Not_Draft()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Backend Engineer", null, Guid.NewGuid(), Now);
        vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        var result = await new GetVacancyHandler(db, new FakePositionProfileReader()).HandleAsync(
            new GetVacancyRequest { CompanyId = companyId, VacancyId = vacancy.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.ApplicationCount);
        Assert.False(result.Value.CanChangePositionProfile);
    }


    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
