using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class SicknessAuthorizationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid PlainEmployeeUser = new("ee000001-0000-0000-0000-000000000001");
    private static readonly Guid ManagerUser = new("ee000001-0000-0000-0000-000000000002");
    private static readonly Guid OtherManagerUser = new("ee000001-0000-0000-0000-000000000003");
    private static readonly Guid HrAdminUser = new("ee000001-0000-0000-0000-000000000004");
    private static readonly Guid CompanyAdministratorUser = new("ee000001-0000-0000-0000-000000000005");

    public SicknessAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, PlainEmployeeUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, OtherManagerUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, OtherManagerUser, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdministratorUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    private async Task<HttpClient> ClientFor(Guid companyId, Guid userId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.SyncCompanyAsync(_factory, userId, companyId);
        return client;
    }


    [Fact]
    public async Task PlainEmployee_Gets_Ok_Listing_Sickness_Categories()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Creating_Sickness_Category()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Cold",
            displayOrder = 1
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Listing_Employee_Sickness_Records()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Recording_Sickness_On_Behalf_Of_Another_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records",
            new
            {
                companyId,
                employeeId = Guid.NewGuid(),
                categoryId = Guid.NewGuid(),
                startDate = "2026-07-01",
                startDayPart = 0
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Ok_Listing_Sickness_Categories()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Creating_Sickness_Category()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = "Cold",
            displayOrder = 1
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Listing_Employee_Sickness_Records()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    [Fact]
    public async Task Manager_Gets_Ok_Viewing_Own_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, ManagerUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{ManagerUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Gets_Forbidden_Viewing_Different_Managers_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, ManagerUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{OtherManagerUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Ok_Viewing_Any_Managers_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, HrAdminUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{ManagerUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Viewing_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{PlainEmployeeUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CompanyAdministrator_Gets_Forbidden_Viewing_Team_Sickness_Today()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, CompanyAdministratorUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{CompanyAdministratorUser}/team-sickness-today");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    [Fact]
    public async Task Employee_Can_Record_And_List_Their_Own_Sickness()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        using var hrClient = await ClientFor(companyId, HrAdminUser);
        var setupResponse = await hrClient.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = $"Category-{Guid.NewGuid():N}",
            displayOrder = 1
        });
        setupResponse.EnsureSuccessStatusCode();
        var category = await setupResponse.Content.ReadFromJsonAsync<CategoryPayload>();

        var recordResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{PlainEmployeeUser}/sickness-records/my",
            new
            {
                companyId,
                employeeId = PlainEmployeeUser,
                categoryId = category!.Id,
                startDate = "2026-07-01",
                startDayPart = 0
            });
        Assert.Equal(HttpStatusCode.Created, recordResponse.StatusCode);

        var listResponse = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{PlainEmployeeUser}/sickness-records/my");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var payload = await listResponse.Content.ReadFromJsonAsync<ListPayload>();
        Assert.NotNull(payload);
        Assert.Single(payload!.Records);
    }

    [Fact]
    public async Task Employee_Gets_Forbidden_Listing_Another_Employees_Own_Sickness_Records()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync(
            $"/api/companies/{companyId}/employees/{Guid.NewGuid()}/sickness-records/my");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }


    [Fact]
    public async Task Manager_Gets_Ok_Getting_Missing_Fit_Notes()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, ManagerUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PlainEmployee_Gets_Forbidden_Getting_Missing_Fit_Notes()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientFor(companyId, PlainEmployeeUser);

        var response = await client.GetAsync($"/api/companies/{companyId}/sickness-evidence-requests/missing");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record CategoryPayload(Guid Id);

    private sealed record SicknessRecordSummaryPayload(
        Guid Id,
        Guid CompanyId,
        Guid EmployeeId,
        Guid CategoryId,
        string Status,
        string StartDate,
        string StartDayPart,
        string? EndDate,
        decimal? TotalDays,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record ListPayload(List<SicknessRecordSummaryPayload> Records);
}
