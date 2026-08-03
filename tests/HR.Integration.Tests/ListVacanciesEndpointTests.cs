using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class ListVacanciesEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000013-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public ListVacanciesEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, RecruiterUser, SystemRoles.Recruiter))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, RecruiterUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    [Fact]
    public async Task Get_Vacancies_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Vacancies_Returns_Empty_List_When_None_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_Vacancies_Returns_PositionProfile_Fields_When_Linked_Profile_Is_Active()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        Guid vacancyId;
        Guid positionProfileId;
        Guid departmentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            departmentId = Guid.NewGuid();
            var department = Department.Create(departmentId, companyId, "Engineering", null, Now);
            var positionProfile = PositionProfile.Create(
                Guid.NewGuid(), companyId, departmentId, locationId: Guid.NewGuid(), "Backend Engineer",
                description: "Owns the payments platform", probationMonthsOverride: null,
                workingDaysOverride: null, hoursPerDayOverride: null, salaryMin: null, salaryMax: null,
                salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), Now);
            employeesDb.Departments.Add(department);
            employeesDb.PositionProfiles.Add(positionProfile);
            await employeesDb.SaveChangesAsync();
            positionProfileId = positionProfile.Id;

            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancy);
            await recruitmentDb.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(vacancyId, item.Id);
        Assert.Equal(positionProfileId, item.PositionProfileId);
        Assert.Equal("Backend Engineer", item.PositionProfileTitle);
        Assert.Equal(departmentId, item.PositionProfileDepartmentId);
        // No AdvertTitle override was supplied, so EffectiveTitle falls back to the Position Profile's.
        Assert.Equal("Backend Engineer", item.EffectiveTitle);
    }

    [Fact]
    public async Task Get_Vacancies_EffectiveTitle_Prefers_AdvertTitle_Override_Over_PositionProfile_Title()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(
                Guid.NewGuid(), companyId, referenceData.PositionProfileId,
                "Advert Title Override", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancy);
            await recruitmentDb.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal("Advert Title Override", item.AdvertTitle);
        Assert.Equal("Advert Title Override", item.EffectiveTitle);
    }

    [Fact]
    public async Task Get_Vacancies_Gracefully_Resolves_PositionProfile_Fields_When_Linked_Profile_Is_Deactivated()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        Guid vacancyId;
        Guid positionProfileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var positionProfile = PositionProfile.Create(
                Guid.NewGuid(), companyId, Guid.NewGuid(), locationId: Guid.NewGuid(), "Legacy Support Engineer",
                description: null, probationMonthsOverride: null, workingDaysOverride: null,
                hoursPerDayOverride: null, salaryMin: null, salaryMax: null, salaryType: null,
                defaultLeavePolicyId: Guid.NewGuid(), Now);
            positionProfile.Deactivate(Now);
            employeesDb.PositionProfiles.Add(positionProfile);
            await employeesDb.SaveChangesAsync();
            positionProfileId = positionProfile.Id;

            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, "Legacy Support Engineer", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancy);
            await recruitmentDb.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(vacancyId, item.Id);
        Assert.Equal("Legacy Support Engineer", item.PositionProfileTitle);
        // List items don't carry an IsActive field of their own — Title/DepartmentId still
        // resolve for a deactivated linked profile is the behaviour under test here.
    }

    [Fact]
    public async Task Get_Vacancies_Isolates_By_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), otherCompanyId, Guid.NewGuid(), "Product Designer", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancy);
            await recruitmentDb.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    [Fact]
    public async Task Get_Vacancies_Filtered_By_PositionProfileId_Returns_Only_Matching_Vacancies()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        Guid matchingVacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var matching = Vacancy.Create(
                Guid.NewGuid(), companyId, referenceData.PositionProfileId, "Matching Role", null, Guid.NewGuid(), Now);
            var other = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), "Other Role", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.AddRange(matching, other);
            await recruitmentDb.SaveChangesAsync();
            matchingVacancyId = matching.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies?PositionProfileId={referenceData.PositionProfileId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(matchingVacancyId, item.Id);
    }

    [Fact]
    public async Task Get_Vacancies_Filtered_By_DepartmentId_Returns_Only_Vacancies_In_That_Department()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        Guid matchingVacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var otherDepartment = Department.Create(Guid.NewGuid(), companyId, $"Dept-{Guid.NewGuid():N}", null, Now);
            var otherProfile = PositionProfile.Create(
                Guid.NewGuid(), companyId, otherDepartment.Id, locationId: Guid.NewGuid(), "Other Role", description: null,
                probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
                salaryMin: null, salaryMax: null, salaryType: null, defaultLeavePolicyId: Guid.NewGuid(), Now);
            employeesDb.Departments.Add(otherDepartment);
            employeesDb.PositionProfiles.Add(otherProfile);
            await employeesDb.SaveChangesAsync();

            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var matching = Vacancy.Create(
                Guid.NewGuid(), companyId, referenceData.PositionProfileId, "Matching Department Role", null, Guid.NewGuid(), Now);
            var other = Vacancy.Create(Guid.NewGuid(), companyId, otherProfile.Id, "Other Department Role", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.AddRange(matching, other);
            await recruitmentDb.SaveChangesAsync();
            matchingVacancyId = matching.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies?DepartmentId={referenceData.DepartmentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        var item = Assert.Single(payload!.Items);
        Assert.Equal(matchingVacancyId, item.Id);
    }

    [Fact]
    public async Task Get_Vacancies_With_Filters_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies?PositionProfileId={Guid.NewGuid()}&DepartmentId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record ListPayload(List<VacancyListItemPayload> Items);

    private sealed record VacancyListItemPayload(
        Guid Id,
        Guid PositionProfileId,
        string? AdvertTitle,
        string? PositionProfileTitle,
        Guid? PositionProfileDepartmentId,
        string EffectiveTitle,
        string? EffectiveLocation);
}
