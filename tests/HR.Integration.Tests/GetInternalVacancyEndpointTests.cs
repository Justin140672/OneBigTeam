using System.Net;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class GetInternalVacancyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid EmployeeUser = new("cc000020-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public GetInternalVacancyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUser, SystemRoles.Employee))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, EmployeeUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, EmployeeUser, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<Guid> SeedVacancyAsync(Guid companyId, string advertTitle, bool advertisedInternally, bool open, bool close = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, Guid.NewGuid(), advertTitle, "A description", Guid.NewGuid(), Now,
            assignedRecruiterId: null, isAdvertisedInternally: advertisedInternally);
        if (open)
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        if (close)
            vacancy.Close(Now, DateOnly.FromDateTime(Now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    [Fact]
    public async Task Get_InternalVacancy_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/internal-vacancies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_InternalVacancy_Returns_Open_Advertised_Vacancy_Without_Recruiter_Facing_Fields()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var vacancyId = await SeedVacancyAsync(companyId, "Internal Role", advertisedInternally: true, open: true);

        var response = await client.GetAsync($"/api/companies/{companyId}/internal-vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(vacancyId, doc.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("Internal Role", doc.RootElement.GetProperty("title").GetString());

        foreach (var forbidden in new[] { "hiringManagerId", "assignedRecruiterId", "applicationCount", "candidate", "notes" })
            Assert.DoesNotContain(doc.RootElement.EnumerateObject(), p => string.Equals(p.Name, forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(45000, 55000, true)]
    [InlineData(45000, null, true)]
    [InlineData(null, 55000, true)]
    [InlineData(null, null, false)]
    public async Task Get_InternalVacancy_Returns_Salary_From_Linked_PositionProfile(int? min, int? max, bool hasType)
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        Guid vacancyId;
        using (var scope = _factory.Services.CreateScope())
        {
            var employeesDb = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            var profile = PositionProfile.Create(
                Guid.NewGuid(), companyId, Guid.NewGuid(), locationId: Guid.NewGuid(), "Salaried Role",
                probationMonthsOverride: null, workingDaysOverride: null, hoursPerDayOverride: null,
                salaryMin: min, salaryMax: max, salaryType: hasType ? SalaryType.Annual : null,
                defaultLeavePolicyId: Guid.NewGuid(), Now);
            employeesDb.PositionProfiles.Add(profile);
            await employeesDb.SaveChangesAsync();

            var recruitmentDb = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, profile.Id, "Salaried Role", "A description", Guid.NewGuid(), Now,
                assignedRecruiterId: null, isAdvertisedInternally: true);
            vacancy.Open(Now, DateOnly.FromDateTime(Now.UtcDateTime));
            recruitmentDb.Vacancies.Add(vacancy);
            await recruitmentDb.SaveChangesAsync();
            vacancyId = vacancy.Id;
        }

        var response = await client.GetAsync($"/api/companies/{companyId}/internal-vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(min, ReadDecimal(doc.RootElement, "salaryMin"));
        Assert.Equal(max, ReadDecimal(doc.RootElement, "salaryMax"));
        Assert.Equal(hasType ? "Annual" : null, ReadString(doc.RootElement, "salaryType"));
    }

    private static decimal? ReadDecimal(JsonElement element, string name) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    [Fact]
    public async Task Get_InternalVacancy_Returns_NotFound_For_Draft_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var vacancyId = await SeedVacancyAsync(companyId, "Draft Role", advertisedInternally: true, open: false);

        var response = await client.GetAsync($"/api/companies/{companyId}/internal-vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_InternalVacancy_Returns_NotFound_For_Open_But_Not_Advertised_Vacancy()
    {
        var companyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var vacancyId = await SeedVacancyAsync(companyId, "Not Advertised", advertisedInternally: false, open: true);

        var response = await client.GetAsync($"/api/companies/{companyId}/internal-vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_InternalVacancy_Returns_NotFound_For_Vacancy_In_Another_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await AuthenticatedClient(companyId);
        var vacancyId = await SeedVacancyAsync(otherCompanyId, "Other Company Role", advertisedInternally: true, open: true);

        var response = await client.GetAsync($"/api/companies/{companyId}/internal-vacancies/{vacancyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
