using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class GetVacancyEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid RecruiterUser = new("cc000012-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetVacancyEndpointTests(ApiWebApplicationFactory factory)
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
    public async Task Get_Vacancy_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/vacancies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_Vacancy_Returns_NotFound_When_Vacancy_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_Vacancy_Returns_PositionProfile_Fields_When_Linked_Profile_Is_Active()
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
                Guid.NewGuid(), companyId, departmentId, locationId: null, "Backend Engineer",
                description: "Owns the payments platform", probationMonthsOverride: null,
                workingDaysOverride: null, hoursPerDayOverride: null, salaryMin: null, salaryMax: null,
                salaryType: null, defaultLeavePolicyId: null, Now);
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

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(vacancyId, payload!.Id);
        Assert.Equal(positionProfileId, payload.PositionProfileId);
        Assert.Equal("Backend Engineer", payload.PositionProfileTitle);
        Assert.Equal(departmentId, payload.PositionProfileDepartmentId);
        Assert.Equal("Owns the payments platform", payload.PositionProfileDescription);
        Assert.True(payload.PositionProfileIsActive);
    }

    [Fact]
    public async Task Get_Vacancy_Gracefully_Resolves_PositionProfile_Fields_When_Linked_Profile_Is_Deactivated()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        Guid vacancyId;
        Guid positionProfileId;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var positionProfile = PositionProfile.Create(
                Guid.NewGuid(), companyId, null, locationId: null, "Legacy Support Engineer",
                description: "Legacy on-call support", probationMonthsOverride: null,
                workingDaysOverride: null, hoursPerDayOverride: null, salaryMin: null, salaryMax: null,
                salaryType: null, defaultLeavePolicyId: null, Now);
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

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Legacy Support Engineer", payload!.PositionProfileTitle);
        Assert.Equal("Legacy on-call support", payload.PositionProfileDescription);
        Assert.False(payload.PositionProfileIsActive);
    }

    [Fact]
    public async Task Get_Vacancy_EffectiveLocation_Is_Resolved_Purely_From_PositionProfile_Location()
    {
        // Location is no longer a vacancy-level concept at all — there is no vacancy-level override
        // field, and EffectiveLocation is resolved exclusively from the linked Position Profile's
        // PositionProfileSummary.LocationName.
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        Guid vacancyId;
        string locationName;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            locationName = (await employeesDb.Locations.SingleAsync(l => l.Id == referenceData.LocationId)).Name;

            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(
                Guid.NewGuid(), companyId, referenceData.PositionProfileId,
                "Backend Engineer", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancy);
            await recruitmentDb.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(referenceData.PositionProfileId, payload!.PositionProfileId);
        Assert.Equal(locationName, payload.EffectiveLocation);
    }

    [Fact]
    public async Task Get_Vacancy_Returns_ApplicationCount_Zero_And_CanChangePositionProfile_True_For_Draft_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        Guid vacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, referenceData.PositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancy);
            await recruitmentDb.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(0, payload!.ApplicationCount);
        Assert.True(payload.CanChangePositionProfile);
    }

    [Fact]
    public async Task Get_Vacancy_Returns_ApplicationCount_And_CanChangePositionProfile_False_When_Vacancy_Has_An_Application()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var referenceData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        Guid vacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, referenceData.PositionProfileId, "Backend Engineer", null, Guid.NewGuid(), Now);
            recruitmentDb.Vacancies.Add(vacancy);
            var candidate = Candidate.Create(Guid.NewGuid(), companyId, "Jane", "Doe", $"jane.doe.{Guid.NewGuid():N}@example.com", null, null, Now);
            recruitmentDb.Candidates.Add(candidate);
            recruitmentDb.Applications.Add(Application.Create(Guid.NewGuid(), companyId, vacancy.Id, candidate.Id, null, Now));
            await recruitmentDb.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<VacancyPayload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.ApplicationCount);
        Assert.False(payload.CanChangePositionProfile);
    }

    private sealed record VacancyPayload(
        Guid Id,
        Guid CompanyId,
        Guid PositionProfileId,
        string? AdvertTitle,
        string? AdvertDescription,
        string? PositionProfileTitle,
        Guid? PositionProfileDepartmentId,
        string? PositionProfileDescription,
        bool? PositionProfileIsActive,
        string EffectiveTitle,
        string? EffectiveLocation,
        int ApplicationCount,
        bool CanChangePositionProfile);
}
